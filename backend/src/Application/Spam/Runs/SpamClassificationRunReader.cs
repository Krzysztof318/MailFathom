// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Spam.Runs;

/// <summary>Reports where one account's whole-mailbox classification run has got to.</summary>
/// <remarks>
/// Reading a run is separated from asking for one — <see cref="SpamClassificationRunRequests" /> — because the two are
/// reached under different grants: a run asked to act moves mail on somebody's server, while watching one is a report of
/// what this deployment is doing. What each message was classified as is neither, and is
/// <see cref="History.SpamClassificationHistory" /> under the audit grant.
/// </remarks>
public sealed class SpamClassificationRunReader
{
    private readonly ISpamClassificationRunStore runs;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the run store.</summary>
    /// <param name="runs">Holds the one run an account may have outstanding, and the ending of the last one.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public SpamClassificationRunReader(ISpamClassificationRunStore runs, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runs = runs;
        this.authorization = authorization;
    }

    /// <summary>Reads the run one account has outstanding, or the last one it finished.</summary>
    /// <param name="accountId">The account whose run is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run, or <see langword="null" /> where the account has never been asked for one.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<SpamClassificationRun?> FindLatestAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.runs.FindLatestAsync(accountId, cancellationToken);
    }
}
