// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>One email a lexical search matched, with why it ranked where it did and what matched.</summary>
/// <remarks>
/// <para>
/// The summary is the same bounded projection a listing returns, so a caller can act on a result — read the message,
/// filter its folder, recognize its sender — without a second query. What search adds is the rank and the snippets, and
/// neither is anything the mailbox holds: both are computed per query and mean nothing outside the one that produced
/// them.
/// </para>
/// <para>
/// The snippets are mail content and inherit the classification of the message they were cut from. They are bounded in
/// number and length by <see cref="EmailSearchSnippetBounds" />, and there may be none of them: a message whose body
/// yielded no text — encrypted mail, or mail whose content lives entirely in an attachment — matched on its subject or
/// its participants, which the summary already publishes whole.
/// </para>
/// </remarks>
/// <param name="Summary">The email as a listing shows it.</param>
/// <param name="RelevanceRank">What the full-text ranking scored this email against this query, higher being more relevant.</param>
/// <param name="Snippets">The highlighted extracts of the body around what matched, in the order the body carries them.</param>
public sealed record EmailSearchMatch(
    EmailSummary Summary,
    float RelevanceRank,
    IReadOnlyList<string> Snippets)
{
    /// <summary>Gets where the email sits in the timeline order, which is what breaks a tie between equal ranks.</summary>
    public EmailTimelinePosition Position => this.Summary.Position;
}
