// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Reports where one scope's re-derivation has got to, or how the last one ended.</summary>
/// <remarks>
/// Reading a run is separated from asking for one — <see cref="StoredMailRederivationRequests" /> — because the two are
/// reached under different grants: asking makes the deployment walk a whole mailbox, while watching one is a report of
/// what the deployment is already doing.
/// </remarks>
public sealed class StoredMailRederivationRunReader
{
    private readonly IStoredMailRederivationRunStore runs;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the run store.</summary>
    /// <param name="runs">Holds the one run a scope may have, outstanding or ended.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredMailRederivationRunReader(
        IStoredMailRederivationRunStore runs,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runs = runs;
        this.authorization = authorization;
    }

    /// <summary>Reads the run one scope has outstanding, or the last one it finished.</summary>
    /// <param name="scope">The account, and the one folder of it, whose run is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run, or <see langword="null" /> where the scope has never been asked for one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<StoredMailRederivationRun?> FindAsync(StoredMailScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.runs.FindAsync(scope, cancellationToken);
    }
}
