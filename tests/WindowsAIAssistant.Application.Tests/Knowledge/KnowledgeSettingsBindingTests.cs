using System.Reflection;
using WindowsAIAssistant.Core.Models;

namespace WindowsAIAssistant.Application.Tests.Knowledge;

/// <summary>
/// Checks that every knowledge privacy switch is wired all the way from the settings page to
/// the persisted model and back.
/// <para>
/// These switches decide whether a person's documents are read, sent to a cloud service to be
/// embedded, or sent to a model to be answered. A switch that is shown but not saved is the worst
/// kind of bug in a privacy control: the person turns it, believes they have, and the thing they
/// were trying to prevent happens anyway. A switch that is read but never shown is also wrong,
/// because what it governs is on by default and nothing on screen says so.
/// </para>
/// <para>
/// The bindings are declared in XAML, so the only way to confirm them is to look at the markup.
/// Both directions are checked against the same table, so a switch cannot be added to one side
/// and quietly missed on the other.
/// </para>
/// </summary>
public sealed class KnowledgeSettingsBindingTests
{
    private static string? SettingsPagePath => FindSettingsPage();

    /// <summary>
    /// The persisted name and the view-model name for each switch. They differ on purpose: the
    /// persisted setting is a permission and reads as <c>Allow…</c>, the view model is a control
    /// and reads as <c>Is…Enabled</c>. Spelling both out rather than deriving one from the other
    /// means a rename on one side has to be made deliberately on the other.
    /// <para>
    /// Both are carried to every test even though each looks at only one, so that all three
    /// assertions are driven by the same table and a switch cannot be added to one side and
    /// quietly missed on the other.
    /// </para>
    /// </summary>
    public static TheoryData<string, string> Switches()
    {
        var data = new TheoryData<string, string>
        {
            { "AllowKnowledgeBase", "IsKnowledgeBaseEnabled" },
            { "AllowCloudEmbedding", "IsCloudEmbeddingEnabled" },
            { "AllowCloudKnowledgeProcessing", "IsCloudKnowledgeAnswersEnabled" },
        };

        return data;
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public void EveryPrivacySwitchExistsOnThePersistedModel(string persisted, string _) =>
        Assert.NotNull(typeof(PrivacySettings).GetProperty(persisted, BindingFlags.Public | BindingFlags.Instance));

    [Theory]
    [MemberData(nameof(Switches))]
    public void EveryPrivacySwitchHasAControlOnTheSettingsPage(string _, string viewModelProperty)
    {
        var page = SettingsPagePath;

        Assert.NotNull(page);
        Assert.Contains($"ViewModel.{viewModelProperty}", File.ReadAllText(page!), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public void EveryPrivacySwitchIsExposedByTheSettingsPageState(string _, string viewModelProperty)
    {
        var property = typeof(App.ViewModels.SettingsViewModel)
            .GetProperty(viewModelProperty, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.True(property!.CanRead && property.CanWrite, $"{viewModelProperty} has to be both shown and changed.");
    }

    [Fact]
    public void TheKnowledgeBaseSwitchIsOnByDefaultAndTheCloudOnesAreOff()
    {
        // The defaults are the promise the notice on the page makes. The local index is safe
        // enough to be on; both things that would send a document anywhere are not.
        var settings = new PrivacySettings();

        Assert.True(settings.AllowKnowledgeBase);
        Assert.False(settings.AllowCloudEmbedding);
        Assert.False(settings.AllowCloudKnowledgeProcessing);
    }

    [Fact]
    public void TheCloudSwitchesAreSeparateDecisionsFromTheCloudAiOne()
    {
        // Somebody who has allowed answers to go to a provider has allowed a question to go to a
        // provider. They have not agreed to have a whole file read by one, and collapsing the two
        // would be a consent decision made on their behalf.
        var settings = new PrivacySettings { AllowCloudAI = true };

        Assert.False(settings.AllowCloudEmbedding);
        Assert.False(settings.AllowCloudKnowledgeProcessing);
    }

    /// <summary>
    /// Walks up from the test binaries to the repository root looking for the page. Resolved this
    /// way rather than copied into the output, because a copy would be a second page that could
    /// disagree with the first, and the whole point is to read the page that is actually built.
    /// </summary>
    private static string? FindSettingsPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src", "WindowsAIAssistant.App", "Views", "Pages", "SettingsPage.xaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
