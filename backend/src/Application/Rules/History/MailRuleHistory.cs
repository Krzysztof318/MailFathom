// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Rules.History;

/// <summary>Reads what this deployment's rules concluded about one account's mail.</summary>
/// <remarks>
/// <para>
/// The store is written by every evaluation pass and aged by retention. This is the reading, separated so that the grant
/// is asked where the history is served: what a rule concluded about a message is derived from that message, which is
/// why it is published under the audit grant rather than under the one covering reports of the deployment's own state.
/// </para>
/// <para>
/// Which rules are loaded is a different question and a different grant — <see cref="MailRuleSetReader" /> answers it —
/// because a rule set says what this deployment will do and a history says what it did to somebody's mail.
/// </para>
/// </remarks>
public sealed class MailRuleHistory
{
    private readonly IMailRuleExecutionStore executions;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the recorded executions.</summary>
    /// <param name="executions">Keeps what each evaluation concluded.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailRuleHistory(IMailRuleExecutionStore executions, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(authorization);

        this.executions = executions;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of an account's rule history.</summary>
    /// <param name="query">The account, the filters, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<MailRuleExecutionPage> ReadPageAsync(
        MailRuleExecutionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.executions.ReadPageAsync(query, cancellationToken);
    }
}
