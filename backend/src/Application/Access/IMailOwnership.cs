// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Access;

/// <summary>Answers whose mail a unit of background work is acting on, from what that work already holds.</summary>
/// <remarks>
/// <para>
/// A caller-facing request carries its owner on the principal, and <see cref="AccessAuthorization.RequireOwner" /> is
/// what reads it. A worker carries none: it runs under this process's own identity, which acts for nobody, so a
/// synchronization run, an embedding pass, and a backfill each know an account or a message and never a person. This
/// port is how those reach one, and it exists so that a bound stated per owner is charged to the owner the work is
/// genuinely for rather than to whoever a request happened to admit.
/// </para>
/// <para>
/// Ownership hangs on the mail account, so both reads are the same read at a different distance: a stored email names
/// the account it was synchronized for, and the account names its owner. Neither is derived from configuration, which
/// is what keeps the answer true once accounts are declared per owner rather than per deployment.
/// </para>
/// </remarks>
public interface IMailOwnership
{
    /// <summary>Reads the owner one mail account belongs to.</summary>
    /// <param name="accountId">The account whose owner is asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owner the account belongs to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no owner can be established for the account.</exception>
    /// <remarks>
    /// An account this deployment serves but has never synchronized has no row yet — the row is written by whichever
    /// run first binds one of its folders — so the answer for one is the owner a configured account belongs to. That
    /// is the same resolution the row's creation performs, which is what keeps a run bounded before its first folder
    /// is bound and after it identically.
    /// </remarks>
    Task<MailOwnerId> ReadAccountOwnerAsync(MailAccountId accountId, CancellationToken cancellationToken);

    /// <summary>Reads the owner one locally stored email belongs to.</summary>
    /// <param name="storedEmailId">The stored email whose owner is asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owner the message belongs to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no message is stored under that identifier.</exception>
    /// <remarks>
    /// Asked once per message rather than per provider call, because the answer cannot change while a message is being
    /// worked on and the call it precedes costs orders of magnitude more than the read.
    /// </remarks>
    Task<MailOwnerId> ReadStoredEmailOwnerAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);
}
