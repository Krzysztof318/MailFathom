// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Starts the move of stored content into the bucket, stops it, and starts it again.</summary>
/// <remarks>
/// <para>
/// Three decisions and no work. What each of them writes is the state a bounded pass reads before it copies anything, so
/// an operator's terminal neither carries the move nor keeps it alive — which is what makes every one of these answer
/// immediately however much mail the deployment holds, and what makes a paused move stay paused across a restart.
/// </para>
/// <para>
/// Asking a deployment to rewrite where its mail is held is work it performs on request, which is the grant it asks for.
/// Reading how far it has come is a different grant, and neither implies the other.
/// </para>
/// </remarks>
public sealed class StoredContentMoveControl
{
    private readonly IStoredContentMoveRunStore runStore;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;
    private readonly IEmailContentObjectBackend? objectBackend;

    /// <summary>Initializes the control.</summary>
    /// <param name="runStore">Reads the move this deployment has, and records what the operator decided about it.</param>
    /// <param name="commitPolicy">Makes the read and the write one decision, and resolves a race with a running pass.</param>
    /// <param name="timeProvider">Stamps the request.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="objectBackend">The bucket a move would carry content into, absent where the deployment configured none.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// The object backend is asked for rather than required, and its presence is the whole of what
    /// <see cref="IsAvailable" /> reports — the same idiom the content store selects its backend by. A deployment
    /// storing content in the database has nowhere to move it to, and that is a fact about its configuration rather than
    /// a failure to raise while composing the graph.
    /// </remarks>
    public StoredContentMoveControl(
        IStoredContentMoveRunStore runStore,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        AccessAuthorization authorization,
        IEmailContentObjectBackend? objectBackend = null)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runStore = runStore;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
        this.objectBackend = objectBackend;
    }

    /// <summary>Gets whether this deployment has an object backend a move could carry its content into.</summary>
    public bool IsAvailable => this.objectBackend is not null;

    /// <summary>Asks for every payload the database still holds to be carried into the bucket.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The move the deployment now has, which is the one already under way when there was one.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deployment configured no object-storage endpoint, which <see cref="IsAvailable" /> reports.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when two requests raced past the bounded retries.</exception>
    /// <remarks>
    /// A move that is running or paused is answered with itself rather than replaced, and that is the point: starting
    /// over would discard a paused operator's position and walk everything behind it a second time. What starts a fresh
    /// move is a deployment that has none, or one whose last move finished — which is also how the payloads a move
    /// refused to carry are reached again once whatever refused them has been repaired.
    /// </remarks>
    public Task<StoredContentMoveRun> StartAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        if (!this.IsAvailable)
        {
            throw new InvalidOperationException(
                "Stored content cannot be moved by a deployment that names no object-storage endpoint. Configure ContentStorage:ObjectStorage and select that backend first.");
        }

        return this.DecideAsync(
            existing => existing is { IsOutstanding: true }
                ? existing
                : new StoredContentMoveRun
                {
                    RequestedAt = this.timeProvider.GetUtcNow(),
                    State = StoredContentMoveState.Running,
                    Kind = EmailContentKind.IncomingMessage,
                },
            cancellationToken);
    }

    /// <summary>Stops the move where it is, leaving everything it has already carried exactly as it is.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The move as it now stands, or <see langword="null" /> when this deployment has never been asked for one.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <remarks>
    /// Nothing is cancelled. A pass that is running finishes the payload it holds — which is one message, already put
    /// and verified — and the next one finds the move stopped. A move that has finished is left as it is rather than
    /// reported as an error: there is nothing to pause, and saying so is the answer.
    /// </remarks>
    public Task<StoredContentMoveRun?> PauseAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.DecideIfPresentAsync(
            existing => existing.State is StoredContentMoveState.Running
                ? existing with { State = StoredContentMoveState.Paused }
                : existing,
            cancellationToken);
    }

    /// <summary>Sets a paused move going again from the position it stopped at.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The move as it now stands, or <see langword="null" /> when this deployment has never been asked for one.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deployment configured no object-storage endpoint, which <see cref="IsAvailable" /> reports.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <remarks>
    /// A move that finished is not resumed, because its walk reached the end of the content: what reaches the payloads
    /// it left behind is a further move, which <see cref="StartAsync" /> begins.
    /// </remarks>
    public Task<StoredContentMoveRun?> ResumeAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        if (!this.IsAvailable)
        {
            throw new InvalidOperationException(
                "Stored content cannot be moved by a deployment that names no object-storage endpoint. Configure ContentStorage:ObjectStorage and select that backend first.");
        }

        return this.DecideIfPresentAsync(
            existing => existing.State is StoredContentMoveState.Paused
                ? existing with { State = StoredContentMoveState.Running }
                : existing,
            cancellationToken);
    }

    /// <summary>Reads the move, decides what it becomes, and commits the two as one.</summary>
    /// <remarks>
    /// One committed decision rather than a read followed by a write, because a pass commits its progress onto the same
    /// row: the loser of that race is retried from a fresh read and decides again, which is what keeps an operator's
    /// pause from being written over by the counts of the pass it stopped.
    /// </remarks>
    private Task<StoredContentMoveRun> DecideAsync(
        Func<StoredContentMoveRun?, StoredContentMoveRun> decide,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var existing = await this.runStore.FindAsync(attemptCancellationToken);
                var decided = decide(existing);

                if (decided != existing)
                {
                    await this.runStore.SaveAsync(session, decided, attemptCancellationToken);
                }

                return decided;
            },
            cancellationToken);

    /// <summary>Decides about the move this deployment has, and answers with nothing when it has none.</summary>
    private Task<StoredContentMoveRun?> DecideIfPresentAsync(
        Func<StoredContentMoveRun, StoredContentMoveRun> decide,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.runStore.FindAsync(attemptCancellationToken) is not { } existing)
                {
                    return null;
                }

                var decided = decide(existing);

                if (decided != existing)
                {
                    await this.runStore.SaveAsync(session, decided, attemptCancellationToken);
                }

                return decided;
            },
            cancellationToken);
}
