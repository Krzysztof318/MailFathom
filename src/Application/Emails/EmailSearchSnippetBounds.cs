// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;

namespace MailMcp.Application.Emails;

/// <summary>How much of a message's body one search result may show.</summary>
/// <remarks>
/// <para>
/// Snippets are where search meets mail content, so this is the data-minimization boundary of the whole use case: a
/// result publishes several bounded extracts around the words that matched, never the body they came from. Without the
/// bound a search would be a way to read every message that matches a common word, which is not what a caller asked for
/// and not what the retention and access design assumes a search does.
/// </para>
/// <para>
/// The bounds are deployment configuration rather than request input. A caller who could raise them could lift the
/// control that limits how much mail one query draws out, and the useful values depend on how a deployment's mail is
/// written rather than on what any single request wants.
/// </para>
/// </remarks>
public sealed record EmailSearchSnippetBounds
{
    /// <summary>The greatest number of extracts one result may carry.</summary>
    public const int MaximumSnippetsPerEmail = 10;

    /// <summary>The greatest number of words one extract may carry.</summary>
    public const int MaximumWordsPerSnippet = 100;

    /// <summary>The fewest words one extract may carry, which is also the floor <see cref="WordsPerSnippet" /> is validated against.</summary>
    /// <remarks>Below this an extract shows a matched word with no surrounding text, which tells a reader nothing a rank does not.</remarks>
    public const int MinimumWordsPerSnippet = 4;

    private EmailSearchSnippetBounds(int snippetsPerEmail, int wordsPerSnippet)
    {
        this.SnippetsPerEmail = snippetsPerEmail;
        this.WordsPerSnippet = wordsPerSnippet;
    }

    /// <summary>Gets the bounds a deployment that configures none receives.</summary>
    /// <remarks>Three extracts of about a line each: enough to see why a message matched, and far short of reading it.</remarks>
    public static EmailSearchSnippetBounds Default { get; } = new(3, 24);

    /// <summary>Gets how many extracts one result may carry.</summary>
    public int SnippetsPerEmail { get; }

    /// <summary>Gets how many words one extract may carry.</summary>
    public int WordsPerSnippet { get; }

    /// <summary>Creates bounds from what a deployment configured.</summary>
    /// <param name="snippetsPerEmail">How many extracts one result may carry.</param>
    /// <param name="wordsPerSnippet">How many words one extract may carry.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either value is outside the range this type accepts.</exception>
    /// <remarks>
    /// Configuration is checked here as well as by the options validation at startup, because these values are the
    /// privacy control rather than a preference: nothing that constructs them may reach a query with a bound nobody
    /// checked, whichever path the value arrived by.
    /// </remarks>
    public static EmailSearchSnippetBounds Create(int snippetsPerEmail, int wordsPerSnippet)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(snippetsPerEmail, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(snippetsPerEmail, MaximumSnippetsPerEmail);
        ArgumentOutOfRangeException.ThrowIfLessThan(wordsPerSnippet, MinimumWordsPerSnippet);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wordsPerSnippet, MaximumWordsPerSnippet);

        return new EmailSearchSnippetBounds(snippetsPerEmail, wordsPerSnippet);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} snippets of at most {1} words",
        this.SnippetsPerEmail,
        this.WordsPerSnippet);
}
