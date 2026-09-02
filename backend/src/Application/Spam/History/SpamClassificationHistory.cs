// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Spam.History;

/// <summary>Reads what classification concluded about one account's mail.</summary>
/// <remarks>
/// A verdict, a score, and the signals behind them are derived from the message they are about, so this is published
/// under the audit grant rather than the one covering reports of the deployment's own state. Where a run has got to is
/// the other question and the other grant, which <see cref="Runs.SpamClassificationRunReader" /> answers.
/// </remarks>
public sealed class SpamClassificationHistory
{
    private readonly ISpamClassificationHistoryReader classifications;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the recorded classifications.</summary>
    /// <param name="classifications">Keeps what each classification concluded.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public SpamClassificationHistory(
        ISpamClassificationHistoryReader classifications,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(authorization);

        this.classifications = classifications;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of an account's classifications.</summary>
    /// <param name="query">The account, the filters, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<SpamClassificationHistoryPage> ReadPageAsync(
        SpamClassificationHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.classifications.ReadPageAsync(query, cancellationToken);
    }
}
