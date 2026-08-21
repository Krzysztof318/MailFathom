// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Reads one account's record of the changes MailFathom made to its mailbox, for whoever may read it.</summary>
/// <remarks>
/// <para>
/// The store keeps the trail and answers every writer of it, including the mutation paths that append to it while a run
/// is under way. This is the use case reading it on somebody's behalf, and it exists so that the grant is asked where
/// the reading is rather than only at the route that happens to serve it today: what the trail says is where a person's
/// mail has been, when, and at whose instruction, so an entrypoint added later must not be able to publish it by
/// forgetting a filter.
/// </para>
/// <para>
/// It narrows nothing of its own. The query the caller composed already bounds the page and names the account, and a
/// second opinion here would be a bound nobody could see from the caller's side.
/// </para>
/// </remarks>
public sealed class MailboxMutationAuditTrailReader
{
    private readonly IMailboxMutationAuditEntryStore entries;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the trail's store.</summary>
    /// <param name="entries">Keeps the recorded changes.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxMutationAuditTrailReader(IMailboxMutationAuditEntryStore entries, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(authorization);

        this.entries = entries;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of an account's audit trail.</summary>
    /// <param name="query">The account, the filters, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<MailboxMutationAuditPage> ReadPageAsync(
        MailboxMutationAuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.entries.ReadPageAsync(query, cancellationToken);
    }
}
