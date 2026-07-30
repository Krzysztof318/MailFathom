// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;

namespace MailMcp.Application.Emails;

/// <summary>How many ranked emails one lexical search returns.</summary>
/// <remarks>
/// <para>
/// A search returns a window rather than a page, and this bound is what closes it. Relevance order is not stable the
/// way a timeline order is — indexing one new message can move every rank — so a cursor into a ranked result set would
/// name a boundary that no longer means what it meant when it was issued. The specification states the bound instead of
/// implying a cursor that would not be sound, and a caller who needs more than a window narrows the structured filters.
/// </para>
/// <para>
/// The maximum is lower than a timeline page's, because a ranked result costs more than a listed one: PostgreSQL builds
/// a highlighted extract per row on top of matching and ranking it. It is also the bound on how much mail content one
/// request can draw out of a mailbox, which is the reason it is a control rather than a tuning knob.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and carries a value of zero, which names no window.
/// <see cref="IsSpecified" /> reports that. Every limit a search uses arrives through <see cref="Create" /> or
/// <see cref="FromRequested" />, so the default cannot reach a query from a request.
/// </para>
/// </remarks>
public readonly record struct EmailSearchResultLimit
{
    /// <summary>The greatest number of ranked results one search returns.</summary>
    public const int MaximumValue = 50;

    /// <summary>The number of results a request that names none receives.</summary>
    /// <remarks>Smaller than the maximum deliberately: relevance ordering means the first results are the ones worth reading, and a caller that has not thought about the count gets the window that costs least to produce.</remarks>
    public const int DefaultValue = 20;

    private EmailSearchResultLimit(int value) => this.Value = value;

    /// <summary>Gets the limit a request that names none receives.</summary>
    public static EmailSearchResultLimit Default { get; } = new(DefaultValue);

    /// <summary>Gets how many ranked results the search returns.</summary>
    public int Value { get; }

    /// <summary>Gets whether this value names a limit rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Value is not 0;

    /// <summary>Creates a limit from what a request asked for.</summary>
    /// <param name="value">The requested number of results.</param>
    /// <returns>The validated limit.</returns>
    /// <exception cref="EmailSearchResultLimitOutOfRangeException">Thrown when <paramref name="value" /> is below one or above <see cref="MaximumValue" />.</exception>
    public static EmailSearchResultLimit Create(int value) => value is >= 1 and <= MaximumValue
        ? new EmailSearchResultLimit(value)
        : throw new EmailSearchResultLimitOutOfRangeException(value, MaximumValue);

    /// <summary>Creates a limit from a request that may not have named one.</summary>
    /// <param name="value">The requested number of results, or <see langword="null" /> when the request named none.</param>
    /// <returns>The validated limit, or <see cref="Default" /> when the request named none.</returns>
    /// <exception cref="EmailSearchResultLimitOutOfRangeException">Thrown when <paramref name="value" /> is named and outside the accepted range.</exception>
    public static EmailSearchResultLimit FromRequested(int? value) => value is { } requested
        ? Create(requested)
        : Default;

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString(CultureInfo.InvariantCulture);
}
