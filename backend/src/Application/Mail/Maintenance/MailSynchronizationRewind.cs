// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Discards an account's durable synchronization progress so its next run reads its folders afresh.</summary>
/// <remarks>
/// <para>
/// The expensive half of filling in properties a newer release added. The forward pass asks a server only about UIDs
/// above the folder's checkpoint and the backward pass reconciles what disappeared, so mail already mirrored keeps
/// whatever shape it had on the day it arrived — and the only thing that makes a run read it again is the progress it
/// resumes from no longer being there. Everything the server knows is then re-read, including what the stored payload
/// never carried: flags, keywords, the internal date, whatever a later release starts recording from the envelope.
/// </para>
/// <para>
/// What it costs is the whole scope off the wire, back through MIME extraction, and back into the content store, which
/// is why the count is read separately and put in front of the operator before anything is discarded.
/// <see cref="StoredMailRederivation" /> is the cheap answer wherever the property is already in the stored payload.
/// </para>
/// <para>
/// Nothing here reaches a mail server, and nothing here removes mail. Re-reading an occurrence upserts the local email
/// it already stored rather than storing a second one, so what a rewind costs is a fetch rather than a mailbox.
/// </para>
/// </remarks>
public sealed class MailSynchronizationRewind
{
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly IStoredMailCounter storedMailCounter;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the rewind.</summary>
    /// <param name="checkpointStore">Reads and removes the durable progress of the account's bindings.</param>
    /// <param name="storedMailCounter">Counts what a rewound scope would have re-read.</param>
    /// <param name="concurrencyRetryPolicy">Commits the removal, retrying a conflict with a competing writer.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailSynchronizationRewind(
        ISynchronizationCheckpointStore checkpointStore,
        IStoredMailCounter storedMailCounter,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(storedMailCounter);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(authorization);

        this.checkpointStore = checkpointStore;
        this.storedMailCounter = storedMailCounter;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.authorization = authorization;
    }

    /// <summary>Reports how much mail a rewind of one scope would have the next runs read again.</summary>
    /// <param name="scope">The account, and the one folder of it, the rewind would cover.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many stored emails the scope holds.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It is what the scope holds rather than what a run would fetch, and the two differ: mail the server no longer
    /// holds is not fetched again, and mail that arrived since is fetched without ever having been stored. Neither
    /// difference is knowable without a mailbox session, which this deliberately does not open, and the stored count is
    /// the figure of the right order for the decision an operator is making.
    /// <para>
    /// It counts rather than discards, so it asks for the permission a report of the deployment's own state is published
    /// under and never for the one that performs the rewind.
    /// </para>
    /// </remarks>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    public Task<int> AssessAsync(StoredMailScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.storedMailCounter.CountStoredEmailsAsync(scope, cancellationToken);
    }

    /// <summary>Discards the durable progress of the scope's bindings in one transaction.</summary>
    /// <param name="scope">The account, and the one folder of it, whose progress is discarded.</param>
    /// <param name="cancellationToken">Cancels the removal before or during its single transaction.</param>
    /// <returns>The aliases whose bindings held progress, ordered and without repeats.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <remarks>
    /// One transaction for the whole scope rather than a bounded pass per folder. What it removes is one row per
    /// binding, so an account's worth of it is a handful of rows however much mail those bindings cover, and a partial
    /// rewind would leave an account whose folders resume from different points for no benefit.
    /// <para>
    /// It makes the deployment pull a mailbox over IMAP again, which is asking it to do work it can already do, so the
    /// operating grant is what reaches it and reading the assessment beforehand does not.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MailFolderAlias>> RewindAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        IReadOnlyList<MailFolderAlias> rewound = [];

        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) => rewound =
                await this.checkpointStore.DiscardCheckpointsAsync(
                    persistenceSession,
                    scope.Account,
                    scope.Folder,
                    attemptCancellationToken),
            cancellationToken);

        return rewound;
    }
}
