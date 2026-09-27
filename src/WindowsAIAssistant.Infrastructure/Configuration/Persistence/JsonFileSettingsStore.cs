using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Configuration;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Configuration.Persistence;

/// <summary>
/// Keeps the person's settings in one JSON file under their local application data.
/// <para>
/// The file is written in the same shape as the shipped configuration, sections and all, so
/// it is also registered as a configuration source. A saved value and a value typed into
/// appsettings.json therefore reach the application by exactly the same route, which is what
/// keeps a saved setting from behaving like a second-class citizen.
/// </para>
/// <para>
/// The write goes to a temporary file that is then moved into place. A crash, a full disk, or
/// a closed laptop lid halfway through a save leaves the previous settings readable instead of
/// a half-written file that would fail to parse on the next launch.
/// </para>
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    /// <summary>The folder name used under the person's local application data.</summary>
    public const string FolderName = "WindowsAIAssistant";

    /// <summary>The file name used inside that folder.</summary>
    public const string FileName = "user-settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ILogger<JsonFileSettingsStore> _logger;
    private readonly string _path;

    public JsonFileSettingsStore(ILogger<JsonFileSettingsStore> logger)
        : this(logger, DefaultPath())
    {
    }

    /// <summary>
    /// Initializes a store over an explicit path. Tests use this so they never touch the
    /// settings of the person running them.
    /// </summary>
    public JsonFileSettingsStore(ILogger<JsonFileSettingsStore> logger, string path)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _logger = logger;
        _path = path;
    }

    /// <inheritdoc />
    public string Location => _path;

    /// <summary>
    /// Gets the path the settings are kept at when nothing else is specified.
    /// </summary>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        FolderName,
        FileName);

    /// <inheritdoc />
    public async Task<Result> SaveAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            var temporaryPath = _path + ".tmp";

            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);

            // The move replaces the previous file in one step, so a reader never sees a
            // partially written document.
            File.Move(temporaryPath, _path, overwrite: true);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporaryFile();
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteTemporaryFile();
            _logger.LogError(exception, "Settings could not be written to {Path}.", _path);
            return Result.Failure("Settings could not be saved. Check that the folder is writable and try again.");
        }
    }

    private void TryDeleteTemporaryFile()
    {
        try
        {
            var temporaryPath = _path + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is harmless: the next save overwrites it, and nothing
            // ever reads it.
            _logger.LogDebug(exception, "A temporary settings file could not be removed.");
        }
    }
}
