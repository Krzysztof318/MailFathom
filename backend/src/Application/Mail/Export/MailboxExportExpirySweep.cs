// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Domain.Access;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.Mail.Export;

/// <summary>Deletes the archives whose retention period has run out, one bounded pass at a time.</summary>
/// <remarks>
/// <para>
/// Expiry is read from what each export recorded when it finished rather than from a timer held in one process, which
/// is what makes it survive a restart and a replica ending: a deployment that was down for a day deletes on its next
/// pass every archive that came due while it was.
/// </para>
/// <para>
/// The pass is one run for the whole deployment, so it runs only under the lease on its own scope, as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. A replica refused the lease deletes nothing and asks again on its next interval.
/// </para>
/// <para>
/// The record is written before the object is deleted, which is the ordering every deletion path here uses: a record
/// saying the archive is gone while the object is still there leaves an orphan the reclamation removes, and the
/// opposite order would leave a record offering a download of bytes that are not there.
/// </para>
/// </remarks>
public sealed class MailboxExportExpirySweep
{
    /// <summary>The lease the sweep is held under, which is the deployment's as the sweep is.</summary>
    internal static readonly WorkScope SweepScope = WorkScope.Create("mailbox-export-expiry");

    /// <summary>How many archives one pass deletes at most.</summary>
    /// <remarks>Each deletion is one request to the content store, so the bound is what keeps a deployment that let a hundred exports come due at once from spending a pass on all of them.</remarks>
    public const int MaximumArchivesPerRun = 50;

    private readonly AccessAuthorization authorization;
    private readonly IMailboxExportStore exports;
    private readonly IMailboxExportArchiveStore archives;
    private readonly IMailboxExportAuditor auditor;
    private readonly IWorkLeaseRunner leases;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the sweep.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="exports">Names what has come due and records that it expired.</param>
    /// <param name="archives">Removes the archives.</param>
    /// <param name="auditor">Records each expiry, because an archive going is an act on somebody's mailbox copy.</param>
    /// <param name="leases">Holds the sweep's scope for the length of one pass.</param>
    /// <param name="timeProvider">Reads the instant expiry is judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailboxExportExpirySweep(
        AccessAuthorization authorization,
        IMailboxExportStore exports,
        IMailboxExportArchiveStore archives,
        IMailboxExportAuditor auditor,
        IWorkLeaseRunner leases,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.exports = exports;
        this.archives = archives;
        this.auditor = auditor;
        this.leases = leases;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs one bounded pass if this replica takes the sweep's lease.</summary>
    /// <param name="cancellationToken">Cancels the claim and the pass.</param>
    /// <returns>How many archives the pass deleted, or <see langword="null" /> when another replica holds the sweep.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when anything but this deployment's own process reached the use case.</exception>
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        var expired = 0;

        var ran = await this.leases.TryRunUnderLeaseAsync(
            SweepScope,
            async heldToken => expired = await this.ExpireAsync(heldToken),
            cancellationToken);

        return ran ? expired : null;
    }

    private async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        var asOf = this.timeProvider.GetUtcNow();

        var due = await this.exports.FindDueForExpiryAsync(asOf, MaximumArchivesPerRun, cancellationToken);
        var expired = 0;

        foreach (var export in due)
        {
            var applied = await this.exports.SaveAsync(
                export with
                {
                    State = MailboxExportState.Expired,
                    ObjectLocator = null,
                },
                MailboxExportState.Completed,
                cancellationToken);

            if (!applied)
            {
                // Downloaded and deleted, or expired by a pass that ran beside this one. Either way the archive is
                // somebody else's to remove, and removing it twice is not this pass's to decide.
                continue;
            }

            if (export.ObjectLocator is { } locator)
            {
                await this.archives.DeleteAsync(locator, cancellationToken);
            }

            await this.auditor.RecordAsync(
                new MailboxExportAct(
                    Caller: "mailfathom",
                    MailFathomPermission.AdminExport,
                    MailboxExportActKind.Expired,
                    export.Account,
                    export.FolderPath,
                    export.Id,
                    export.MessageCount,
                    export.ByteCount,
                    asOf),
                cancellationToken);

            expired++;
        }

        return expired;
    }
}
