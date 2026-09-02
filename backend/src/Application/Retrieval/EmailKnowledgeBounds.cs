// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Retrieval;

/// <summary>How much mail one retrieval may hand to a model.</summary>
/// <remarks>
/// <para>
/// The ceiling on what leaves the process for a question, expressed where the passages are built rather than where they
/// are sent. A model asks for context by writing a query, so nothing about the request bounds the answer — this does,
/// and it does so before the passages exist, which is what makes the bound impossible to widen from the model's side.
/// </para>
/// <para>
/// Two numbers rather than one total, because they control different things. The count is how many messages one question
/// can draw on, and the per-passage size is how much of any single message it can draw out; a single total would let one
/// enormous extract satisfy the same ceiling as a spread across several messages.
/// </para>
/// </remarks>
public sealed record EmailKnowledgeBounds
{
    private EmailKnowledgeBounds(int maximumPassages, int maximumCharactersPerPassage)
    {
        this.MaximumPassages = maximumPassages;
        this.MaximumCharactersPerPassage = maximumCharactersPerPassage;
    }

    /// <summary>Gets the bounds a deployment that states none receives.</summary>
    /// <remarks>
    /// <para>
    /// The count is <see cref="EmailSearchResultLimit.DefaultValue" /> rather than a smaller number of its own, because
    /// the comparison somebody actually makes is between asking a question and searching for the answer: a run reaching
    /// fewer messages than one <c>search_emails</c> window holds answers worse than the search it was supposed to spare
    /// the caller, and it does so on exactly the questions a search already handles.
    /// </para>
    /// <para>
    /// Matching it costs nothing this type protects. What bounds how much of a mailbox one question can reach is the
    /// run's own ceiling on retrieved characters, applied across every lookup a model makes; these two bound one lookup,
    /// and a per-lookup count below the search window is not what keeps a run small.
    /// </para>
    /// </remarks>
    public static EmailKnowledgeBounds Default { get; } = new(EmailSearchResultLimit.DefaultValue, 1200);

    /// <summary>Gets the greatest number of passages one retrieval may return.</summary>
    public int MaximumPassages { get; }

    /// <summary>Gets the greatest number of characters one passage may carry.</summary>
    public int MaximumCharactersPerPassage { get; }

    /// <summary>Creates bounds, refusing values no retrieval could run under.</summary>
    /// <param name="maximumPassages">The greatest number of passages one retrieval may return.</param>
    /// <param name="maximumCharactersPerPassage">The greatest number of characters one passage may carry.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either value is outside the range this type accepts.</exception>
    /// <remarks>
    /// The passage count is capped by what one search can rank, because a retrieval is answered from a search window and
    /// asking for more passages than that window holds would state a bound no run could reach.
    /// </remarks>
    public static EmailKnowledgeBounds Create(int maximumPassages, int maximumCharactersPerPassage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPassages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPassages, EmailSearchResultLimit.MaximumValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharactersPerPassage, 1);

        return new EmailKnowledgeBounds(maximumPassages, maximumCharactersPerPassage);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} passages of at most {1} characters",
        this.MaximumPassages,
        this.MaximumCharactersPerPassage);
}
