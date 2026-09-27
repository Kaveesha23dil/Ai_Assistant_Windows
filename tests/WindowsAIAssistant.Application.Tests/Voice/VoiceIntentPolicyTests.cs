using WindowsAIAssistant.Application.Voice;

namespace WindowsAIAssistant.Application.Tests.Voice;

/// <summary>
/// The voice policy is replaced when a person saves a change, while the pipeline holds the
/// same object for the life of the session. These tests cover that hand-over.
/// </summary>
public sealed class VoiceIntentPolicyTests
{
    [Fact]
    public void ANewPolicyStartsFromTheSafeDefaults()
    {
        var policy = new VoiceIntentPolicy();

        Assert.True(policy.Enabled);
        Assert.True(policy.SpeakResponses);
        Assert.True(policy.ConfirmLowConfidenceCommands);

        // Continuous microphone access is never on until it is asked for.
        Assert.False(policy.EnableContinuousListening);
    }

    [Fact]
    public void UpdatingReplacesEveryValueAtOnce()
    {
        var policy = new VoiceIntentPolicy();
        var before = policy.Values;

        policy.Update(new VoiceIntentPolicyValues
        {
            Enabled = false,
            SpeakResponses = false,
            Language = "de-DE",
        });

        // A reader either sees the old decisions or the new ones. A half-applied set would be
        // read as a real configuration, which is the whole reason values are swapped whole.
        Assert.NotSame(before, policy.Values);
        Assert.False(policy.Enabled);
        Assert.False(policy.SpeakResponses);
        Assert.Equal("de-DE", policy.Language);
    }

    [Fact]
    public void UpdatingLeavesAnythingNotSpecifiedAtItsDefault()
    {
        var policy = new VoiceIntentPolicy();

        policy.Update(new VoiceIntentPolicyValues { Enabled = false });

        Assert.Equal(240, policy.MaximumSpokenResponseLength);
        Assert.Equal(0.70, policy.MinimumCommandConfidence);
    }

    [Fact]
    public void UpdatingRejectsMissingValues()
    {
        var policy = new VoiceIntentPolicy();

        Assert.Throws<ArgumentNullException>(() => policy.Update(null!));
    }
}
