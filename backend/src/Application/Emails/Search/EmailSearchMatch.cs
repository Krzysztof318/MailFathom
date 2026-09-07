// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search.Attachments;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search;

/// <summary>One email a search matched, with why it ranked where it did and what matched.</summary>
/// <remarks>
/// <para>
/// The summary is the same bounded projection a listing returns, so a caller can act on a result — read the message,
/// filter its folder, recognize its sender — without a second query. What search adds is the rank and the extracts, and
/// none of it is anything the mailbox holds: all of it is computed per query and means nothing outside the one that
/// produced it.
/// </para>
/// <para>
/// The snippets are mail content and inherit the classification of the message they were cut from. They are bounded in
/// number and length by <see cref="EmailSearchSnippetBounds" />, and there may be none of them: a message whose body
/// yielded no text — encrypted mail, or mail whose content lives entirely in an attachment — matched on its subject or
/// its participants, which the summary already publishes whole.
/// </para>
/// <para>
/// What an attachment contributed stays out of those snippets and travels in
/// <see cref="AttachmentMatches" /> instead. A snippet is an extract of what the message says, so quoting a file into
/// one would report words the body never carried and leave a reader unable to say which file they came from.
/// </para>
/// </remarks>
/// <param name="Summary">The email as a listing shows it.</param>
/// <param name="RelevanceRank">What the ranking scored this email against this query, higher being more relevant.</param>
/// <param name="Snippets">The highlighted extracts of the body around what matched, in the order the body carries them.</param>
public sealed record EmailSearchMatch(
    EmailSummary Summary,
    float RelevanceRank,
    IReadOnlyList<string> Snippets)
{
    /// <summary>Gets what the message's attachments contributed, each naming its file and the place inside it.</summary>
    /// <remarks>
    /// Empty on the two occasions there is nothing to say: a message whose attachments the query never reached, and one
    /// this deployment has not read attachments of at all. It is a property beside the ranking rather than a parameter
    /// of it because the window read that produces a match knows nothing about files — the attachments of a window are
    /// read once, over the window, after it is closed.
    /// </remarks>
    public IReadOnlyList<EmailAttachmentMatch> AttachmentMatches { get; init; } = [];

    /// <summary>Gets whether a model's description of an attached picture is the whole of this message's claim on the query.</summary>
    /// <remarks>
    /// Never true of a message any word of the query or any passage somebody wrote reached, because such a message is
    /// placed by that passage and the picture contributes nothing to where it sits — which is the guarantee
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
    /// states as a partition of the result rather than as a weight inside it.
    /// </remarks>
    public bool IsDepictedMatch { get; init; }

    /// <summary>Gets where the email sits in the timeline order, which is what breaks a tie between equal ranks.</summary>
    public EmailTimelinePosition Position => this.Summary.Position;
}
