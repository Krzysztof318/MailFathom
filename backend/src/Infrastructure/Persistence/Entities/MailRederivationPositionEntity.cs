// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>How far one operator-asked re-derivation of a scope's stored mail has walked.</summary>
/// <remarks>
/// <para>
/// A table of its own rather than a row in <see cref="BackfillPositionEntity" />, because that walk is one per
/// deployment and named by a constant while this one is one per scope an operator names. Keying it by the scope is what
/// lets two accounts be refreshed independently and stops one operator's cursor from stepping another's walk forward.
/// </para>
/// <para>
/// A row exists only while a walk is unfinished. An invocation that reaches the end of its scope removes the row, so
/// asking for the same scope after a later release again starts at the beginning — which is what the command exists for.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRederivationPositionEntity
{
    /// <summary>The folder value a whole-account scope is keyed by.</summary>
    /// <remarks>
    /// A primary key holds no null, and the scope genuinely has two shapes, so the account-wide walk needs a value of
    /// its own. The empty string is safe as that value because a folder alias is validated non-blank everywhere one is
    /// created, so no folder can ever be keyed by it.
    /// </remarks>
    internal const string WholeAccountFolder = "";

    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account this walk belongs to.</summary>
    public required Guid OwnerId { get; set; }

    public required string FolderAlias { get; set; }

    public Guid LastProcessedStoredEmailId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The row's version, which is what stops one walk's commit from moving another's position backwards.</summary>
    /// <remarks>
    /// The insert of a scope's first position is a race the primary key settles. The updates after it are the same race
    /// one row later and the key says nothing about them: two walks that both read this row commit in whatever order
    /// they finish, and the slower one's earlier position would otherwise be written over the faster one's later one —
    /// which costs no correctness and re-reads the difference on every pass afterwards. The token turns that into a
    /// conflict the retry resolves from a fresh read.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
