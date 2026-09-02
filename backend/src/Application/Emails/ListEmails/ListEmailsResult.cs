// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Synchronization.Checkpoints;

namespace MailFathom.Application.Emails.ListEmails;

/// <summary>One page of a mailbox listing, together with what continues it and how current it is.</summary>
/// <param name="Emails">The page, in the timeline order the request asked for, holding no more than the effective page size.</param>
/// <param name="NextCursor">The cursor that reads the next page, or <see langword="null" /> when this page ended the walk.</param>
/// <param name="FolderFreshness">How current the local copy of each folder in the request's scope is.</param>
/// <param name="IncludedJunkMail">Whether the account's junk folder took part in the listing.</param>
/// <remarks>
/// <para>
/// The absence of a cursor is the end of the result set rather than a hint: the reader establishes it by asking storage
/// for one row beyond the page and finding none, so a caller that stops when the cursor is absent has seen every row
/// exactly once. A present cursor never promises that the next page is non-empty — mail can be expunged between two
/// requests — but continuing from it can never skip or repeat a row.
/// </para>
/// <para>
/// Freshness travels with every page because the listing is served from the local copy whether or not a mail server is
/// reachable, which is what makes stale data explicit instead of indistinguishable from an empty mailbox.
/// </para>
/// <para>
/// Whether junk took part travels with the page for the same reason, and it is reported whichever answer it is: a page
/// that omitted a whole folder and a page that read every folder are otherwise the same shape, so a caller could not
/// tell a mailbox holding nothing more from one whose remainder is behind a flag they did not set.
/// </para>
/// </remarks>
public sealed record ListEmailsResult(
    IReadOnlyList<EmailSummary> Emails,
    string? NextCursor,
    IReadOnlyList<MailboxFolderFreshness> FolderFreshness,
    bool IncludedJunkMail)
{
    /// <summary>Gets whether another page can be read after this one.</summary>
    public bool HasMore => this.NextCursor is not null;
}
