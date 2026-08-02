// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails;
using MailFathom.Application.Emails.SearchEmails;

namespace MailFathom.Mcp.Tools;

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
    [Description("How these results were retrieved. 'lexical' means full-text matching over the words the mail is written in, with no embedding and no model involved; a query term that appears nowhere in a message will not find it however close its meaning. Read this field rather than assuming a retrieval method, because later releases may add others.")]
    public required EmailRetrievalMode RetrievalMode { get; init; }

    /// <summary>Gets how current the local copy of each folder in the request's scope is.</summary>
    [Description("How current the local copy of each folder in the request's scope is, one entry per folder. Read this before concluding that a mailbox holds no matching mail.")]
    public required IReadOnlyList<FolderCopyFreshness> FolderFreshness { get; init; }

    /// <summary>Publishes a window the use case answered.</summary>
    /// <param name="result">The window to publish.</param>
    /// <param name="snippetBounds">How much of a message's body this deployment lets one result show.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> or <paramref name="snippetBounds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The window is bounded again here against the greatest number of results a search serves, for the reason each
    /// extract is: it is the control on how much mail content one call can draw out of a mailbox, and a control a
    /// defective adapter could widen is not one. The bound applied is the absolute maximum rather than the count this
    /// request asked for, which stays the use case's to decide and to refuse.
    /// </remarks>
    public static SearchEmailsToolResult From(SearchEmailsResult result, EmailSearchSnippetBounds snippetBounds)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(snippetBounds);

        return new SearchEmailsToolResult
        {
            Matches =
            [
                .. result.Matches
                    .Take(EmailSearchResultLimit.MaximumValue)
                    .Select(match => SearchedEmailMatch.From(match, snippetBounds)),
            ],
            RetrievalMode = EmailRetrievalMode.Lexical,
            FolderFreshness = [.. result.FolderFreshness.Select(FolderCopyFreshness.From)],
        };
    }
}
