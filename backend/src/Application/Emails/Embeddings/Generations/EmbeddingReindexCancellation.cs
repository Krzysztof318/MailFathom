// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>Stops a reindex, leaving the generation that was serving exactly where it was.</summary>
/// <remarks>
/// The operation exists because a reindex is a decision an operator can regret while it is still running — the wrong
/// model, a bill growing faster than expected — and the honest answer to that is to stop, not to wait for the switch and
/// then pay for a second one. Nothing about retrieval changes: the generation being abandoned was never read.
/// </remarks>
public sealed class EmbeddingReindexCancellation
{
    private readonly IEmbeddingGenerationStore generationStore;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly EmbeddingBackfillSchedule backfillSchedule;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes a new cancellation.</summary>
    /// <param name="generationStore">Reads which generation is being built and abandons it.</param>
    /// <param name="concurrencyRetryPolicy">Commits the transition, retrying a conflict with a competing writer.</param>
    /// <param name="backfillSchedule">Brings the next upkeep pass forward, which is the pass that removes what the abandoned generation holds.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingReindexCancellation(
        IEmbeddingGenerationStore generationStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        EmbeddingBackfillSchedule backfillSchedule,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(backfillSchedule);
        ArgumentNullException.ThrowIfNull(authorization);

        this.generationStore = generationStore;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.backfillSchedule = backfillSchedule;
        this.authorization = authorization;
    }

    /// <summary>Abandons the generation being built, if one is.</summary>
    /// <param name="cancellationToken">Cancels the read and the transition.</param>
    /// <returns>Whether a reindex was abandoned.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>Stopping a run is asking the deployment to do something it can already do, which is the operating grant rather than the one that started the spend.</remarks>
    public async Task<EmbeddingReindexCancellationOutcome> CancelAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);
        if (generations.Building is not { } building)
        {
            return EmbeddingReindexCancellationOutcome.NothingBuilding;
        }

        var abandoned = await this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.generationStore.AbandonAsync(
                persistenceSession,
                building.Id,
                attemptCancellationToken),
            cancellationToken);

        // A reindex that completed between the read and the write took its generation into service, and abandoning that
        // is not what this command means. Nothing was changed, so nothing is removed.
        if (!abandoned)
        {
            return EmbeddingReindexCancellationOutcome.NothingBuilding;
        }

        // What this leaves behind is a generation nothing reads whose partial vectors are personal data with no purpose
        // left, and the pass that removes them is the one an idle interval has just put as much as a quarter of an hour
        // away. The worker cannot observe the row this changed, so the removal is asked for here.
        this.backfillSchedule.BringForward();

        return EmbeddingReindexCancellationOutcome.Cancelled;
    }
}
