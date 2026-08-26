// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
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
/// Ownership hangs on the mail account, and a stored message inherits it from the folder it was synchronized into, so
/// the answer is a column of the message's own row rather than a resolution through the account. There is no read of
/// an account's owner beside it, and there cannot be: an account is identified by its owner and its identifier
/// together, so a caller holding the identifier alone is not holding an account to ask about.
/// </para>
/// </remarks>
public interface IMailOwnership
{
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
