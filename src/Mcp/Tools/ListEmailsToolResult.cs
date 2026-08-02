// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.ListEmails;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes one page of listed emails.</summary>
/// <remarks>
/// The record is the tool's structured output, so its shape is the advertised output schema and its descriptions travel
/// with it. Paging is stated by the presence of <see cref="NextCursor" /> alone: a caller stops when it is absent rather
/// than spending a request to discover an empty page.
/// </remarks>
[Description("One page of email summaries read from the local mailbox copy, with a cursor for the next page and a per-folder statement of how current the copy is.")]
internal sealed record ListEmailsToolResult
{
    /// <summary>Gets the summaries the page contains, in the requested reading order.</summary>
    [Description("The email summaries on this page, in the requested order. Empty when no email matched the filters.")]
    public required IReadOnlyList<ListedEmailSummary> Emails { get; init; }

    /// <summary>Gets the cursor that reads the next page, or <see langword="null" /> when this page is the last one.</summary>
    [Description("An opaque cursor for the next page. Pass it back unchanged as cursor, with the same filters. Null means this page ended the walk. A present cursor does not promise that the next page is non-empty, because mail can be expunged between two calls, but continuing from it never skips or repeats an email.")]
    public string? NextCursor { get; init; }

    /// <summary>Gets how current the local copy of each folder in the request's scope is.</summary>
    [Description("How current the local copy of each folder in the request's scope is, one entry per folder. Read this before concluding that a mailbox holds no matching mail.")]
    public required IReadOnlyList<FolderCopyFreshness> FolderFreshness { get; init; }

    /// <summary>Publishes a page the use case answered.</summary>
    /// <param name="result">The page to publish.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public static ListEmailsToolResult From(ListEmailsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ListEmailsToolResult
        {
            Emails = [.. result.Emails.Select(ListedEmailSummary.From)],
            NextCursor = result.NextCursor,
            FolderFreshness = [.. result.FolderFreshness.Select(FolderCopyFreshness.From)],
        };
    }
}
