// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.SearchEmails;

namespace MailFathom.Application.Retrieval;

/// <summary>What one lookup of an answering run asks for: the text to rank against, and the narrowing written beside it.</summary>
/// <remarks>
/// <para>
/// Every field here is written by a model, and the type exists so that a run can express what a caller of
/// <c>search_emails</c> expresses. A question that is naturally a filter — mail from one person, mail of one week, mail
/// that carries an attachment — is otherwise answerable only by ranking free text across the whole scope, which is the
/// one shape lexical and vector similarity are both weakest at.
/// </para>
/// <para>
/// The fields are deliberately the structured filters of <see cref="SearchEmailsRequest" /> and no others. That
/// request's remaining members are withheld rather than omitted by accident, and each for its own reason:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The accounts and folders are the caller's authorization, so they travel beside a query rather than inside one. A
/// model writes queries and never its own boundary, which is what keeps the scope unreachable from anything an
/// instruction, a retrieved message, or a tool argument can say.
/// </description>
/// </item>
/// <item>
/// <description>
/// Whether the junk folder is read at all is settled when that scope is resolved rather than by a lookup, and an
/// answering run resolves it excluded. Answering is the path the exclusion exists for — mail written to manipulate
/// whoever reads it now has a model reading it — and a caller hunting a wrongly filed message reaches for the listing
/// or the search that can ask for it.
/// </description>
/// </item>
/// <item>
/// <description>
/// The result count is the deployment's bound on how much mail one lookup may draw out. Exposing it would let the one
/// party with an incentive to ask for more mail ask for more mail, so the bound is applied where the passages are built
/// and nothing about a query can widen it.
/// </description>
/// </item>
/// </list>
/// <para>
/// That list is asserted rather than trusted. A test names the withheld members and compares the two types' properties
/// against them, because the filters this type mirrors are added to a published request by work that has no reason to
/// read this file — which is how the two drifted apart before, with these remarks stating a parity that no longer held.
/// </para>
/// <para>
/// Nothing here is validated. The values reach the same use case that validates the published tool's, so a filter this
/// system would refuse from a caller is refused from a model in the same words and by the same code.
/// </para>
/// </remarks>
public sealed record EmailKnowledgeQuery
{
    /// <summary>Gets the text to rank the eligible mail against.</summary>
    public required string QueryText { get; init; }

    /// <summary>Gets the address the sender must carry, in any case, or <see langword="null" /> for any sender.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the fragment the subject must contain, compared without regard to case, or <see langword="null" /> for any subject.</summary>
    /// <remarks>A structured filter over the stored subject, unrelated to <see cref="QueryText" />: it narrows which emails are eligible before any of them is ranked.</remarks>
    public string? SubjectFragment { get; init; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> for no end.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>Gets the remote <c>\Seen</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelySeen { get; init; }

    /// <summary>Gets the remote <c>\Flagged</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsRemotelyFlagged { get; init; }

    /// <summary>Gets the keyword an email must carry, in any case, or <see langword="null" /> for any keyword.</summary>
    public string? Keyword { get; init; }

    /// <summary>Gets whether attachments are required, or <see langword="null" /> for either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Builds a lookup that carries text and no narrowing.</summary>
    /// <param name="queryText">The text to rank the eligible mail against.</param>
    /// <returns>The query.</returns>
    /// <remarks>The shape a question that narrows by nothing produces, which is most of what a broad question asks for.</remarks>
    public static EmailKnowledgeQuery ForText(string queryText) => new() { QueryText = queryText };
}
