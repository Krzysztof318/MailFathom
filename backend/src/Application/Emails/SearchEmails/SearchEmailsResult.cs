// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Synchronization.Checkpoints;

namespace MailFathom.Application.Emails.SearchEmails;

/// <summary>One ranked window of search results, together with how it was ranked and how current the mail behind it is.</summary>
/// <param name="Matches">The matches, most relevant first, holding no more than the effective result limit.</param>
/// <param name="RetrievalMode">How the window was ranked, which can differ between two searches of one instance.</param>
/// <param name="SemanticSearch">What semantic retrieval can do on this instance, which is what says why a lexical answer was lexical.</param>
/// <param name="FolderFreshness">How current the local copy of each folder in the request's scope is.</param>
/// <param name="IncludedJunkMail">Whether the account's junk folder took part in the search.</param>
/// <remarks>
/// <para>
/// The window is the whole result: nothing continues it, and a caller that needs different mail narrows the structured
/// filters or writes a different query rather than paging. Relevance order is recomputed per query and moves as mail is
/// indexed, so a boundary into it would name a position that had stopped meaning what it meant when it was handed out.
/// </para>
/// <para>
/// The retrieval mode travels with the window rather than being asked about separately, because it is a fact about this
/// one answer: an embedding provider that was unreachable for the length of one call leaves that call lexical on an
/// instance that is otherwise hybrid, and a caller reading a capability instead would draw the wrong conclusion about
/// why a message it expected is absent.
/// </para>
/// <para>
/// The capability travels beside it and answers the other half of the question. The mode alone cannot separate an
/// instance that deliberately does not embed from one whose credential expired an hour ago: both answer
/// <see cref="EmailSearchRetrievalMode.Lexical" />, and only one of them is something to fix. Reading the two together
/// is what turns a quietly narrower result into a stated degradation.
/// </para>
/// <para>
/// Freshness travels with every result for the reason it travels with every page: a search is answered from the local
/// copy whether or not a mail server is reachable, and a folder whose synchronization has been failing for a week
/// otherwise looks exactly like a folder holding nothing that matched.
/// </para>
/// <para>
/// Whether junk took part is reported whichever answer it is, for the reason a listing reports it: a window that left a
/// whole folder out looks exactly like one whose query matched nothing in it.
/// </para>
/// </remarks>
public sealed record SearchEmailsResult(
    IReadOnlyList<EmailSearchMatch> Matches,
    EmailSearchRetrievalMode RetrievalMode,
    SemanticSearchCapability SemanticSearch,
    IReadOnlyList<MailboxFolderFreshness> FolderFreshness,
    bool IncludedJunkMail);
