// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>Reads one account's record of the questions this deployment answered from its mailbox.</summary>
/// <remarks>
/// <para>
/// The store is appended to by every answering run and read by whoever an operator provisioned to read it. Separating
/// the reading into a use case is what lets the grant be asked where the record is served rather than only at the route
/// serving it: the record says which of a person's messages a question reached and when, which is derived personal data
/// however little of the mail itself it carries.
/// </para>
/// <para>
/// It reads beside <see cref="Mail.Mutations.Audit.MailboxMutationAuditTrailReader" /> and asks for the same grant, because
/// the two together are what an operator answers "why is this message here" and "why did it answer that" from.
/// </para>
/// </remarks>
public sealed class MailAnsweringAuditTrailReader
{
    private readonly IMailAnsweringAuditEntryStore entries;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the record's store.</summary>
    /// <param name="entries">Keeps the answered questions.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAnsweringAuditTrailReader(IMailAnsweringAuditEntryStore entries, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(authorization);

        this.entries = entries;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of an account's answering record.</summary>
    /// <param name="query">The account, the filters, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<MailAnsweringAuditPage> ReadPageAsync(
        MailAnsweringAuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.entries.ReadPageAsync(query, cancellationToken);
    }
}
