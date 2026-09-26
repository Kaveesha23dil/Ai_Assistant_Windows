using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WindowsAIAssistant.Core.Abstractions.Voice;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Infrastructure.Windows;

/// <summary>
/// Turns a spoken application name into something the machine can actually launch.
/// <para>
/// This class is the security boundary for application launching. A spoken name is only ever
/// turned into a target that appears in the table below or that Windows itself advertises
/// through the Start menu or the application-paths registry key. A name that matches nothing
/// is a failure, and the result handed onward is a path, a URI, or an application identifier.
/// There is deliberately no step that concatenates user input into a command line, which is
/// why "open" followed by anything unrecognized can never become an execution.
/// </para>
/// <para>
/// Aliases let a user say "chrome" or "vs code" instead of a product's full name, and a
/// final whole-word pass means a partial name still resolves without making the table
/// unbounded.
/// </para>
/// </summary>
public sealed class ApplicationResolver : IApplicationResolver
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private static readonly IReadOnlyDictionary<string, KnownApplication> KnownApplications =
        BuildKnownApplications();

    private readonly Lazy<Task<IReadOnlyDictionary<string, ApplicationTarget>>> _discovered;
    private readonly ILogger<ApplicationResolver> _logger;

    public ApplicationResolver(ILogger<ApplicationResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // Scanning the Start menu touches the file system, so it is deferred and performed at
        // most once, and only after a spoken name has missed the built-in table.
        _discovered = new Lazy<Task<IReadOnlyDictionary<string, ApplicationTarget>>>(DiscoverAsync);
    }

    /// <inheritdoc />
    public async Task<Result<ApplicationTarget>> ResolveAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<ApplicationTarget>.Failure("I didn't catch which application you meant.");
        }

        var spoken = name.Trim();

        if (KnownApplications.TryGetValue(spoken, out var known))
        {
            return await MaterializeAsync(known, cancellationToken).ConfigureAwait(false);
        }

        var discovered = await _discovered.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (discovered.TryGetValue(spoken, out var found))
        {
            return Result<ApplicationTarget>.Success(found);
        }

        // A user who says "chrome" should not have to say "google chrome", so the last attempt
        // matches a spoken name against a whole word of a display name.
        foreach (var candidate in discovered.Values.Concat(MaterializeKnownNames()))
        {
            if (MatchesWholeWord(spoken, candidate.Name))
            {
                return Result<ApplicationTarget>.Success(candidate);
            }
        }

        _logger.LogInformation("No known application matched a spoken name.");
        return Result<ApplicationTarget>.Failure($"I couldn't find an application called {spoken}.");
    }

    /// <summary>
    /// Turns a declared entry into a launchable target. A packaged application and a settings
    /// page are already usable as identifiers; a conventional executable has to be located
    /// first, because naming it is not the same as knowing where it lives.
    /// </summary>
    private Task<Result<ApplicationTarget>> MaterializeAsync(
        KnownApplication application,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return application.Kind switch
        {
            ApplicationTargetKind.Uri or ApplicationTargetKind.AppUserModelId =>
                Task.FromResult(Result<ApplicationTarget>.Success(application.ToTarget())),

            _ => Task.FromResult(Locate(application))
        };
    }

    private static Result<ApplicationTarget> Locate(KnownApplication application)
    {
        var fileName = Path.GetFileName(application.Target);

        foreach (var candidate in CandidatePaths(application.Target))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Result<ApplicationTarget>.Success(
                    new ApplicationTarget(application.Name, candidate, ApplicationTargetKind.FilePath));
            }
        }

        if (File.Exists(fileName))
        {
            return Result<ApplicationTarget>.Success(application.ToTarget());
        }

        return Result<ApplicationTarget>.Failure(
            $"{application.Name} doesn't appear to be installed on this computer.");
    }

    /// <summary>
    /// Produces the locations an executable may live in, most authoritative first. The
    /// application-paths key is the location Windows itself uses to resolve a program, and it
    /// is followed by the machine path so a portable install still works.
    /// </summary>
    private static IEnumerable<string> CandidatePaths(string fileName)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            string? registered = null;

            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
                    .OpenSubKey($@"{AppPathsKey}\{fileName}");

                registered = key?.GetValue(null) as string;
            }
            catch (Exception)
            {
                // A locked-down or redirected key simply contributes no candidate; the remaining
                // sources can still locate the program.
            }

            if (!string.IsNullOrWhiteSpace(registered))
            {
                yield return registered.Trim('"');
            }
        }

        var environmentPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(environmentPath))
        {
            yield break;
        }

        foreach (var directory in environmentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length > 0)
            {
                yield return Path.Combine(trimmed, fileName);
            }
        }
    }

    private static bool MatchesWholeWord(string spoken, string displayName) =>
        displayName
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Equals(spoken, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ApplicationTarget> MaterializeKnownNames() =>
        KnownApplications.Values
            .DistinctBy(application => application.Name)
            .Select(application => application.ToTarget());

    private async Task<IReadOnlyDictionary<string, ApplicationTarget>> DiscoverAsync()
    {
        var found = new Dictionary<string, ApplicationTarget>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var folders = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Windows Start Menu"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };

            foreach (var folder in folders)
            {
                await Task.Run(() => ScanFolder(folder, depth: 3, found), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation("Discovered {Count} Start menu applications.", found.Count);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Scanning the Start menu failed; the built-in application list is still available.");
        }

        return found;
    }

    private static void ScanFolder(string folder, int depth, Dictionary<string, ApplicationTarget> found)
    {
        if (depth < 0 || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(name))
            {
                // The shortcut is stored as a path and handed to the shell, which is the
                // component that knows how to follow a link. Nothing here parses a shortcut or
                // reads the command it contains.
                found.TryAdd(name, new ApplicationTarget(name, file, ApplicationTargetKind.FilePath));
            }
        }

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            ScanFolder(child, depth - 1, found);
        }
    }

    /// <summary>
    /// A declared application: its display name, how to launch it, and the identifiers that
    /// refer to it. A packaged application is addressed by its user model ID, a system page by
    /// its URI, and a conventional program by an executable that is located at resolve time.
    /// </summary>
    private sealed record KnownApplication(string Name, string Target, ApplicationTargetKind Kind)
    {
        public ApplicationTarget ToTarget() => new(Name, Target, Kind);
    }

    private static IReadOnlyDictionary<string, KnownApplication> BuildKnownApplications()
    {
        (string Name, string[] Aliases, string Target, ApplicationTargetKind Kind)[] entries =
        [
            ("Google Chrome", ["chrome", "google chrome", "browser"], "chrome.exe", ApplicationTargetKind.FilePath),
            ("Microsoft Edge", ["edge", "microsoft edge", "web browser"], "msedge.exe", ApplicationTargetKind.FilePath),
            ("Mozilla Firefox", ["firefox", "mozilla firefox"], "firefox.exe", ApplicationTargetKind.FilePath),
            ("Spotify", ["spotify"], "Spotify.exe", ApplicationTargetKind.FilePath),
            ("Visual Studio Code", ["vs code", "vscode", "code", "visual studio code", "vs"], "Code.exe", ApplicationTargetKind.FilePath),
            ("Visual Studio", ["visual studio", "vs 2022", "vs2022"], "devenv.exe", ApplicationTargetKind.FilePath),
            ("Notepad", ["notepad", "text editor"], "notepad.exe", ApplicationTargetKind.FilePath),
            ("Paint", ["paint", "mspaint"], "mspaint.exe", ApplicationTargetKind.FilePath),
            ("Windows Terminal", ["terminal", "windows terminal", "console"], "wt.exe", ApplicationTargetKind.FilePath),
            ("PowerShell", ["powershell", "windows powershell", "pwsh"], "powershell.exe", ApplicationTargetKind.FilePath),
            ("Command Prompt", ["command prompt", "cmd", "command line"], "cmd.exe", ApplicationTargetKind.FilePath),
            ("File Explorer", ["file explorer", "explorer", "files", "my computer", "this pc"], "explorer.exe", ApplicationTargetKind.FilePath),
            ("Task Manager", ["task manager"], "taskmgr.exe", ApplicationTargetKind.FilePath),
            ("Control Panel", ["control panel app"], "control.exe", ApplicationTargetKind.FilePath),
            ("Calculator", ["calculator", "calc"], "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ApplicationTargetKind.AppUserModelId),
            ("Settings", ["settings app", "system settings", "windows settings app"], "ms-settings:home", ApplicationTargetKind.Uri),
            ("Microsoft Store", ["microsoft store", "store app", "app store"], "ms-windows-store:", ApplicationTargetKind.Uri),
            ("Xbox", ["xbox", "xbox app", "game bar"], "ms-xbox:", ApplicationTargetKind.Uri),
            ("Mail", ["mail", "outlook", "email"],
                "microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail",
                ApplicationTargetKind.AppUserModelId),
            ("Calendar", ["calendar"],
                "microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.calendar",
                ApplicationTargetKind.AppUserModelId),
            ("Media Player", ["media player", "music app", "groove"],
                "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic",
                ApplicationTargetKind.AppUserModelId),
            ("Photos", ["photos", "photo app", "windows photos"],
                "Microsoft.Windows.Photos_8wekyb3d8bbwe!App",
                ApplicationTargetKind.AppUserModelId)
        ];

        var table = new Dictionary<string, KnownApplication>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, aliases, target, kind) in entries)
        {
            var application = new KnownApplication(name, target, kind);

            // The display name is registered first so a collision with an alias resolves to the
            // application's real name.
            table[name] = application;

            foreach (var alias in aliases)
            {
                table.TryAdd(alias, application);
            }
        }

        return table;
    }
}
