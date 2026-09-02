// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes one ranked window of search results.</summary>
/// <remarks>
/// <para>
/// The record is the tool's structured output, so its shape is the advertised output schema and its descriptions travel
/// with it. The window is the whole result and nothing continues it: relevance order is recomputed per query and moves
/// as mail is indexed, so a cursor into it would name a position that had stopped meaning what it meant when it was
/// handed out. A caller that needs different mail narrows the filters or writes a different query.
/// </para>
/// <para>
/// An empty window is an ordinary response rather than a failure, which is what keeps a search from answering questions
/// about which accounts and folders exist.
/// </para>
/// </remarks>
[Description("One ranked window of emails matching a text search of the local mailbox copy, with how the results were retrieved and a per-folder statement of how current the copy is. The window is the whole result: nothing continues it, and an empty one is a normal answer rather than an error.")]
internal sealed record SearchEmailsToolResult
{
    /// <summary>Gets the matches, most relevant first.</summary>
    [Description("The matched emails, most relevant first, ties broken by the newest received. Empty when nothing matched the query and the filters, which is a normal answer.")]
    public required IReadOnlyList<SearchedEmailMatch> Matches { get; init; }

    /// <summary>Gets how the results were retrieved.</summary>
    [Description("How these results were retrieved. 'lexical' means full-text matching over the words the mail is written in: a query term that appears nowhere in a message will not find it however close its meaning. 'hybrid' means that ranking was combined with a search by embedding similarity, so a message can appear without carrying the query's words. Read this field on every response rather than assuming a mode: the same server answers 'lexical' when its embedding provider is unavailable, and neither mode involves a chat model or rewrites the query.")]
    public required EmailRetrievalMode RetrievalMode { get; init; }

    /// <summary>Gets what semantic retrieval can do on this server.</summary>
    [Description("What this server can do with embeddings, which is what tells you why a 'lexical' answer was lexical. 'inactive' means the server does not embed mail at all, so lexical is the intended and only mode. 'available' means it does and its provider is answering. 'degraded' means it does, but it currently cannot reach the embedding provider or its configuration is wrong, so these results are narrower than the server intends: say so rather than retrying, because nothing about the request caused it and the server's operator has to fix it.")]
    public required SemanticSearchAvailability SemanticSearch { get; init; }

    /// <summary>Gets how current the local copy of each folder in the request's scope is.</summary>
    [Description("How current the local copy of each folder in the request's scope is, one entry per folder. Read this before concluding that a mailbox holds no matching mail.")]
    public required IReadOnlyList<FolderCopyFreshness> FolderFreshness { get; init; }

    /// <summary>Gets whether the account's junk folder took part in the search.</summary>
    [Description("Whether the account's junk folder took part in this search. False means its mail was left out and is reachable by calling again with includeJunkMail set.")]
    public required bool IncludedJunkMail { get; init; }

    /// <summary>Publishes a window the use case answered.</summary>
    /// <param name="result">The window to publish.</param>
    /// <param name="snippetBounds">How much of a message's body this deployment lets one result show.</param>
    /// <param name="accountNames">Reads the name each named account is published under.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" />, <paramref name="snippetBounds" />, or <paramref name="accountNames" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The window is bounded again here against the greatest number of results a search serves, for the reason each
    /// extract is: it is the control on how much mail content one call can draw out of a mailbox, and a control a
    /// defective adapter could widen is not one. The bound applied is the absolute maximum rather than the count this
    /// request asked for, which stays the use case's to decide and to refuse.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result reports a retrieval mode or a semantic capability this contract has no wire value for.</exception>
    public static SearchEmailsToolResult From(
        SearchEmailsResult result,
        EmailSearchSnippetBounds snippetBounds,
        PublishedAccountNames accountNames)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(accountNames);

        return new SearchEmailsToolResult
        {
            Matches =
            [
                .. result.Matches
                    .Take(EmailSearchResultLimit.MaximumValue)
                    .Select(match => SearchedEmailMatch.From(match, snippetBounds, accountNames)),
            ],
            RetrievalMode = Published(result.RetrievalMode),
            SemanticSearch = Published(result.SemanticSearch),
            FolderFreshness = [.. result.FolderFreshness.Select(freshness => FolderCopyFreshness.From(freshness, accountNames))],
            IncludedJunkMail = result.IncludedJunkMail,
        };
    }

    /// <summary>Maps the use case's retrieval mode onto the value this contract publishes.</summary>
    /// <remarks>
    /// A closed mapping rather than a cast, because the two enumerations are separate on purpose: the wire values are
    /// this boundary's to decide, and a mode the application grew without a published name has to fail here rather than
    /// reach a client as a number nobody documented.
    /// </remarks>
    private static EmailRetrievalMode Published(EmailSearchRetrievalMode retrievalMode) => retrievalMode switch
    {
        EmailSearchRetrievalMode.Lexical => EmailRetrievalMode.Lexical,
        EmailSearchRetrievalMode.Hybrid => EmailRetrievalMode.Hybrid,
        _ => throw new ArgumentOutOfRangeException(
            nameof(retrievalMode),
            retrievalMode,
            "The retrieval mode has no published wire value."),
    };

    /// <summary>Maps the use case's semantic capability onto the value this contract publishes.</summary>
    /// <remarks>Closed for the reason the mapping above is closed: a state the application grew without a published name has to fail here rather than reach a client as a number nobody documented.</remarks>
    private static SemanticSearchAvailability Published(SemanticSearchCapability capability) => capability switch
    {
        SemanticSearchCapability.Inactive => SemanticSearchAvailability.Inactive,
        SemanticSearchCapability.Available => SemanticSearchAvailability.Available,
        SemanticSearchCapability.Degraded => SemanticSearchAvailability.Degraded,
        _ => throw new ArgumentOutOfRangeException(
            nameof(capability),
            capability,
            "The semantic search capability has no published wire value."),
    };
}
