// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Synchronization.Checkpoints;

namespace MailFathom.Application.Emails.SearchEmails;

/// <summary>One ranked window of search results, together with how it was ranked and how current the mail behind it is.</summary>
/// <param name="Matches">The matches, most relevant first, holding no more than the effective result limit.</param>
/// <param name="RetrievalMode">How the window was ranked, which can differ between two searches of one instance.</param>
/// <param name="FolderFreshness">How current the local copy of each folder in the request's scope is.</param>
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
/// Freshness travels with every result for the reason it travels with every page: a search is answered from the local
/// copy whether or not a mail server is reachable, and a folder whose synchronization has been failing for a week
/// otherwise looks exactly like a folder holding nothing that matched.
/// </para>
/// </remarks>
public sealed record SearchEmailsResult(
    IReadOnlyList<EmailSearchMatch> Matches,
    EmailSearchRetrievalMode RetrievalMode,
    IReadOnlyList<MailboxFolderFreshness> FolderFreshness);
