// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>What a screen asks for when it opens one conversation.</summary>
/// <remarks>
/// It names a conversation and how much of it to return, and nothing else. A thread is read by membership rather than
/// by filters — the folder somebody happened to be looking at when they opened it is not part of the question, because
/// narrowing by it would cut the half of the exchange that sits in the sent folder.
/// </remarks>
public sealed record BrowseThreadRequest
{
    /// <summary>Gets the conversation to read, which may be one a merge has since folded into another.</summary>
    public required EmailThreadId ThreadId { get; init; }

    /// <summary>Gets how many messages the page may hold, or <see langword="null" /> when the request named none.</summary>
    public int? PageSize { get; init; }

    /// <summary>Gets the cursor a previous page returned, or <see langword="null" /> for the start of the conversation.</summary>
    public string? Cursor { get; init; }
}
