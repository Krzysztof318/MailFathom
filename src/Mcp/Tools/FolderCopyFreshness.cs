// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Synchronization;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes how current the local copy of one folder is.</summary>
/// <remarks>
/// Every tool answers from the local copy and never contacts a mail server, so a result has to say how current that copy
/// is. Without it a caller cannot tell a folder that holds no matching mail from one whose synchronization has been
/// failing for a week, and both look like an answer about the mailbox.
/// </remarks>
[Description("How current the local copy of one folder is. Every MailFathom read answers from this local copy and never contacts a mail server, so a result can be stale or incomplete and this states which.")]
internal sealed record FolderCopyFreshness
{
    /// <summary>Gets the account the folder belongs to.</summary>
    [Description("The configured MailFathom account identifier the folder belongs to.")]
    public required string AccountId { get; init; }

    /// <summary>Gets MailFathom's own name for the folder.</summary>
    [Description("The MailFathom folder alias, such as INBOX.")]
    public required string FolderAlias { get; init; }

    /// <summary>Gets when progress was last committed for the folder, or <see langword="null" /> when it never was.</summary>
    [Description("When synchronization last durably committed progress for this folder, as an ISO 8601 timestamp. Mail that arrived after it may be missing locally. Null when synchronization has never committed progress for the folder, which means its mail may be absent entirely rather than merely out of date.")]
    public DateTimeOffset? SynchronizedAt { get; init; }

    /// <summary>Gets whether synchronization has ever committed progress for the folder.</summary>
    [Description("Whether synchronization has ever committed progress for this folder. False means an empty or short result says nothing about what the mail server holds.")]
    public required bool WasSynchronized { get; init; }

    /// <summary>Publishes the freshness of one folder.</summary>
    /// <param name="freshness">The freshness entry the result carried.</param>
    /// <returns>The wire representation of <paramref name="freshness" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="freshness" /> is <see langword="null" />.</exception>
    public static FolderCopyFreshness From(MailboxFolderFreshness freshness)
    {
        ArgumentNullException.ThrowIfNull(freshness);

        return new FolderCopyFreshness
        {
            AccountId = freshness.AccountId.Value,
            FolderAlias = freshness.FolderAlias.Value,
            SynchronizedAt = freshness.SynchronizedAt,
            WasSynchronized = freshness.WasSynchronized,
        };
    }
}
