// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Records that an alias started naming a different remote folder.</summary>
/// <remarks>
/// A rebinding makes a folder synchronize from the beginning under a new generation, which an operator sees as a
/// mailbox suddenly reprocessing itself. The audit record is the explanation, and it is the only place a remote folder
/// path is written outside the database, because a path can carry personal or organizational information.
/// <para>
/// The port exists as a deliberately narrow surface: the sink behind it is undecided — a log today, an audit table or
/// an external evidence store once compliance evidence is collected — and one operation is all a caller may reach for,
/// so no other code path acquires a way to write a folder path outside the database.
/// </para>
/// </remarks>
public interface IMailFolderMappingChangeAuditor
{
    /// <summary>Records one mapping change.</summary>
    /// <param name="change">The alias, both remote paths, and the generation the change started.</param>
    /// <param name="cancellationToken">Cancels writing the record.</param>
    /// <returns>A task that completes once the record is durable for the configured sink.</returns>
    Task RecordMappingChangeAsync(MailFolderMappingChange change, CancellationToken cancellationToken);
}

/// <summary>Describes one change of the remote folder an alias names.</summary>
/// <param name="AccountId">The account whose alias was rebound.</param>
/// <param name="Alias">The operator-facing folder name that kept its meaning.</param>
/// <param name="PreviousRemotePath">The remote folder the alias named before, or <see langword="null" /> when this is its first binding.</param>
/// <param name="NewRemotePath">The remote folder the alias names now.</param>
/// <param name="Generation">The generation the new binding synchronizes under.</param>
/// <param name="OccurredAt">When the change was resolved.</param>
public sealed record MailFolderMappingChange(
    MailAccountId AccountId,
    MailFolderAlias Alias,
    RemoteFolderPath? PreviousRemotePath,
    RemoteFolderPath NewRemotePath,
    MailFolderResolutionGeneration Generation,
    DateTimeOffset OccurredAt);
