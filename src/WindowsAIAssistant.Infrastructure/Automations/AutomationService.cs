using System.Text;
using System.Text.Json;
using WindowsAIAssistant.Core.Abstractions.Automations;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Automations;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Infrastructure.Automations;

/// <summary>Single-process scheduler with atomic local persistence and bounded folder processing.</summary>
public sealed class AutomationService : IAutomationService
{
    private readonly string _path;
    private readonly IDocumentAnalysisService _documents;
    private readonly IDocumentTypeDetector _types;
    private readonly IPermissionService _permissions;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<Guid> _running = [];
    private List<Automation>? _items;

    public AutomationService(string path, IDocumentAnalysisService documents,
        IDocumentTypeDetector types, IPermissionService permissions, TimeProvider? clock = null)
    {
        _path = path;
        _documents = documents;
        _types = types;
        _permissions = permissions;
        _clock = clock ?? TimeProvider.System;
    }

    public event EventHandler<Automation>? Completed;

    private async Task LoadAsync(CancellationToken token)
    {
        if (_items is not null) return;
        if (!File.Exists(_path)) { _items = []; return; }
        await using var stream = File.OpenRead(_path);
        var loaded = await JsonSerializer.DeserializeAsync<List<Automation>>(stream, cancellationToken: token)
            ?? throw new InvalidDataException("The automations file is empty or invalid.");
        _items = loaded.Select(item => item.Status == "Running"
            ? item with { Status = "Interrupted", LastResult = "The app closed during this run. Use Run now to retry." }
            : item).ToList();
    }

    private async Task PersistAsync(List<Automation> items, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, items, cancellationToken: token);
        File.Move(temporary, _path, overwrite: true);
        _items = items;
    }

    public async Task<IReadOnlyList<Automation>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await LoadAsync(cancellationToken); return _items!.ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(Automation automation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(automation.Title) || automation.Title.Length > 200)
            throw new ArgumentException("Enter a title of up to 200 characters.");
        if (!Enum.IsDefined(automation.Kind) || !Enum.IsDefined(automation.Repeat))
            throw new ArgumentException("Choose a supported action and schedule.");
        if (automation.Kind == AutomationKind.Reminder && string.IsNullOrWhiteSpace(automation.Details))
            throw new ArgumentException("Enter a reminder message.");
        if (automation.Details.Length > 4000) throw new ArgumentException("Details must be at most 4000 characters.");
        if (automation.Kind == AutomationKind.FolderSummary && !Directory.Exists(automation.Details))
            throw new ArgumentException("Choose an existing folder.");
        var next = AutomationSchedule.Next(automation, _clock.GetUtcNow());
        if (next is null) throw new ArgumentException("Choose a future time for a one-time schedule.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            if (_running.Contains(automation.Id)) throw new InvalidOperationException("Wait for the current run to finish.");
            var saved = automation with { NextRunUtc = next, Status = automation.Enabled ? "Scheduled" : "Paused" };
            await PersistAsync([.. _items!.Where(x => x.Id != saved.Id), saved], cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            if (_running.Contains(id)) throw new InvalidOperationException("Wait for the current run to finish before deleting it.");
            await PersistAsync(_items!.Where(x => x.Id != id).ToList(), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            var item = _items!.Single(x => x.Id == id);
            var next = enabled ? AutomationSchedule.Next(item, _clock.GetUtcNow()) : item.NextRunUtc;
            if (enabled && next is null) throw new ArgumentException("This one-time schedule has passed. Create a new reminder or use Run now.");
            await PersistAsync(_items!.Select(x => x.Id == id
                ? x with { Enabled = enabled, NextRunUtc = next, Status = enabled ? "Scheduled" : "Paused" } : x).ToList(), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RunDueAsync(CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken);
        await Task.WhenAll(items.Where(x => x.Enabled && x.NextRunUtc <= _clock.GetUtcNow())
            .Select(item => RunInternalAsync(item.Id, dueOnly: true, cancellationToken)));
    }

    public Task RunAsync(Guid id, CancellationToken cancellationToken = default) =>
        RunInternalAsync(id, dueOnly: false, cancellationToken);

    private async Task RunInternalAsync(Guid id, bool dueOnly, CancellationToken token)
    {
        Automation item;
        await _gate.WaitAsync(token);
        try
        {
            await LoadAsync(token);
            var found = _items!.FirstOrDefault(x => x.Id == id);
            if (found is null || _running.Contains(id)) return;
            if (dueOnly && (!found.Enabled || found.NextRunUtc is null || found.NextRunUtc > _clock.GetUtcNow())) return;
            var next = dueOnly ? AutomationSchedule.Next(found, _clock.GetUtcNow()) : found.NextRunUtc;
            item = found with
            {
                LastRunUtc = _clock.GetUtcNow(), Status = "Running", NextRunUtc = next,
                Enabled = dueOnly && found.Repeat == AutomationRepeat.Once ? false : found.Enabled,
            };
            // Claim the occurrence before doing work: reopening never repeats an uncertain cloud call.
            await PersistAsync(_items!.Select(x => x.Id == id ? item : x).ToList(), token);
            _running.Add(id);
        }
        finally { _gate.Release(); }

        var status = "Completed";
        string result;
        try { result = item.Kind == AutomationKind.Reminder ? item.Details : await SummarizeFolderAsync(item.Details, token); }
        catch (OperationCanceledException) { status = "Interrupted"; result = "Run cancelled. Use Run now to retry."; }
        catch (AutomationRunException exception) { status = "Failed"; result = exception.Message; }
        catch (Exception) { status = "Failed"; result = "Could not complete this run. Check the folder, AI configuration, and permissions, then try Run now."; }

        Automation completed;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var current = _items!.Single(x => x.Id == id);
            completed = current with { Status = status, LastResult = result };
            await PersistAsync(_items!.Select(x => x.Id == id ? completed : x).ToList(), CancellationToken.None);
        }
        finally { _running.Remove(id); _gate.Release(); }
        Completed?.Invoke(this, completed);
    }

    private async Task<string> SummarizeFolderAsync(string folder, CancellationToken token)
    {
        foreach (var capability in new[] { PermissionCapability.FileSearch, PermissionCapability.DocumentCloudProcessing })
            if (!_permissions.IsGranted(capability)) throw new AutomationRunException(_permissions.GetDeniedMessage(capability));
        // Do not follow file links or descend into subfolders.
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var files = Directory.EnumerateFiles(folder, "*", options)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
            .Where(path => _types.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).Take(21).ToArray();
        if (files.Length == 0) return "No supported documents were found in this folder (subfolders are excluded).";
        var output = new StringBuilder();
        var successes = 0;
        foreach (var file in files.Take(20))
        {
            token.ThrowIfCancellationRequested();
            if (!_permissions.IsGranted(PermissionCapability.FileSearch) || !_permissions.IsGranted(PermissionCapability.DocumentCloudProcessing))
                throw new AutomationRunException("Document permissions were switched off.");
            var result = await _documents.SummarizeAsync(DocumentAnalysisRequest.Summarize(file), token);
            output.AppendLine($"## {Path.GetFileName(file)}");
            output.AppendLine(result.IsSuccess ? result.Text : result.ErrorMessage);
            if (result.IsSuccess) successes++;
            foreach (var reference in result.References) output.AppendLine(reference);
            foreach (var warning in result.Warnings) output.AppendLine(warning.Message);
            output.AppendLine();
            if (output.Length > 200_000) { output.AppendLine("Result size limit reached; remaining files were skipped."); break; }
        }
        if (files.Length > 20) output.AppendLine("Limited to the first 20 supported files, sorted by name. Subfolders are excluded.");
        if (successes == 0) throw new AutomationRunException("No documents could be summarized.\n\n" + output);
        return output.ToString();
    }

    private sealed class AutomationRunException(string message) : Exception(message);
}
