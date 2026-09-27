namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// The per-request knobs a provider is asked to honour.
/// <para>
/// These are properties of the request rather than of the service because a caller may need to
/// override one of them for a single question. Unset values are left as <see langword="null"/>
/// so the provider applies its own configured default, which keeps the configuration file the
/// single place the defaults are written.
/// </para>
/// <para>
/// A timeout is part of this object for the same reason: it is the one bound that can differ
/// between a quick typed question and a long streaming answer, and it has to be expressible
/// without the caller knowing which provider will run.
/// </para>
/// </summary>
public sealed record AIRequestSettings
{
    /// <summary>Gets the model to use, or <see langword="null"/> to use the configured one.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the sampling temperature, or <see langword="null"/> for the configured one.</summary>
    public double? Temperature { get; init; }

    /// <summary>Gets the token ceiling for the answer, or <see langword="null"/> for the configured one.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Gets the request timeout, or <see langword="null"/> for the configured one.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets a value indicating whether the caller asked for a streamed answer. A provider
    /// that cannot stream reports that fact rather than silently pretending it did.
    /// </summary>
    public bool UseStreaming { get; init; }
}
