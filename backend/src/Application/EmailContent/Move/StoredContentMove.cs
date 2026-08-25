// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Carries the payloads a deployment stored in its database into the bucket, one bounded pass at a time.</summary>
/// <remarks>
/// <para>
/// Selecting the object backend answers where the <em>next</em> payload goes. This answers what becomes of everything
/// stored before that, which is the whole of a mailbox for a deployment that has been synchronizing one for a year: the
/// database keeps the size that motivated the move, and every backup keeps carrying it, until something copies the
/// payloads out.
/// </para>
/// <para>
/// One payload is copied at a time and nothing is held across the steps. The bytes are read, checked against what the
/// row records, put under a key of their own, read back and checked again, and only then does the row point at the
/// object — so <b>no database transaction is ever open across a call to the endpoint</b>, which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// requires and what makes the copy safe to run against a live deployment.
/// </para>
/// <para>
/// A payload that cannot be verified stays exactly where it is. It is counted, its reason is published, and the walk
/// carries on: repointing a row at an object nobody vouched for would turn a storage change into mail that cannot be
/// read, and the worst outcome of a move must be that it did not finish.
/// </para>
/// <para>
/// Every carried payload is held within <see cref="RawMimeMemoryBudget" />, the same process-wide budget synchronization
/// holds messages within. That is what makes the move yield rather than compete: a deployment busy fetching mail leaves
/// the move waiting for room, instead of the two together holding twice the memory either was bounded to.
/// </para>
/// </remarks>
public sealed class StoredContentMove
{
    private readonly IStoredContentMoveRunStore runStore;
    private readonly IStoredContentMoveStore contentStore;
    private readonly IEmailContentObjectBackend objectBackend;
    private readonly RawMimeMemoryBudget memoryBudget;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IStoredContentMoveTelemetry telemetry;
    private readonly StoredContentMoveOptions options;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the move.</summary>
    /// <param name="runStore">Reads what the operator asked for, and records what each pass made of it.</param>
    /// <param name="contentStore">Names the payloads the database still holds, reads one, and repoints it.</param>
    /// <param name="objectBackend">Puts an object and reads it back, so a copy can be verified before a row points at it.</param>
    /// <param name="memoryBudget">Bounds what this process holds of a message while it is carrying one.</param>
    /// <param name="commitPolicy">Commits the run's progress, resolving a race with an operator's decision.</param>
    /// <param name="telemetry">Publishes what the pass carried and what it refused to carry.</param>
    /// <param name="options">Bounds one pass.</param>
    /// <param name="timeProvider">Stamps the instant the walk reached the end of the content.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StoredContentMove(
        IStoredContentMoveRunStore runStore,
        IStoredContentMoveStore contentStore,
        IEmailContentObjectBackend objectBackend,
        RawMimeMemoryBudget memoryBudget,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IStoredContentMoveTelemetry telemetry,
        StoredContentMoveOptions options,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(objectBackend);
        ArgumentNullException.ThrowIfNull(memoryBudget);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runStore = runStore;
        this.contentStore = contentStore;
        this.objectBackend = objectBackend;
        this.memoryBudget = memoryBudget;
        this.commitPolicy = commitPolicy;
        this.telemetry = telemetry;
        this.options = options;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
    }

    /// <summary>Runs one bounded pass of the move, if the deployment has one to carry.</summary>
    /// <param name="cancellationToken">Cancels the pass between payloads.</param>
    /// <returns>What this pass carried, and whether the database still holds payloads behind it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when anything but this deployment's own process reached the use case.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels while a payload is being carried, however many the pass had already carried and committed before it.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when committing this pass's progress raced an operator's decision past the bounded retries.</exception>
    /// <remarks>
    /// <para>
    /// It asks for no permission and requires the process itself instead, exactly as every other walk this deployment
    /// drives on its own does. What reaches it is a worker rather than a caller, so there is nobody here to hold a
    /// grant; the operator's grant is asked for where the operator is, which is
    /// <see cref="StoredContentMoveControl" />.
    /// </para>
    /// <para>
    /// A move that is paused, finished, or was never asked for reports an idle pass and touches nothing, and the state
    /// is read again between payloads. That is what makes pausing immediate without cancelling anything: the pass
    /// finishes the payload it holds and ends there, rather than carrying the rest of what its ceilings would have
    /// allowed after somebody asked it to stop.
    /// </para>
    /// <para>
    /// Cancellation between payloads ends the pass rather than raising, and cancellation <em>during</em> one still
    /// commits what the pass reached before the exception leaves: what a pass repointed is durable on its own, so the
    /// worst a stopped pass costs is the one payload it was carrying — which the next pass carries from the beginning.
    /// </para>
    /// </remarks>
    public async Task<StoredContentMovePass> RunAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        if (await this.runStore.FindAsync(cancellationToken) is not { State: StoredContentMoveState.Running } run)
        {
            return StoredContentMovePass.Idle;
        }

        using var pass = this.telemetry.BeginPass();

        var walk = new WalkState(run.Kind, run.ResumeAfter);
        var pending = new Queue<DatabaseBackedPayload>();

        try
        {
            while (walk.CarriedPayloadCount < this.options.PayloadsPerPass
                && walk.ReachedByteCount < this.options.MaxBytesPerPass
                && !cancellationToken.IsCancellationRequested)
            {
                if (pending.Count == 0)
                {
                    var batch = await this.contentStore.GetPayloadsToMoveAsync(
                        walk.Kind,
                        walk.ResumeAfter,
                        this.options.PayloadsPerPass - walk.CarriedPayloadCount,
                        cancellationToken);

                    if (batch.Count == 0)
                    {
                        if (NextKindAfter(walk.Kind) is not { } nextKind)
                        {
                            walk.ReachedEnd = true;
                            pass.ReachedEndOfContent();

                            break;
                        }

                        walk.Kind = nextKind;
                        walk.ResumeAfter = null;

                        continue;
                    }

                    foreach (var payload in batch)
                    {
                        pending.Enqueue(payload);
                    }
                }

                var carried = pending.Dequeue();

                await this.CarryAsync(carried, walk, pass, cancellationToken);

                walk.ResumeAfter = carried.PayloadId;

                if (await this.StoppedByOperatorAsync(run))
                {
                    break;
                }
            }
        }
        finally
        {
            await this.RecordAsync(run, walk);
        }

        return new StoredContentMovePass(
            walk.CopiedPayloadCount,
            walk.FailedPayloadCount,
            walk.MovedByteCount,
            PayloadsRemain: !walk.ReachedEnd);
    }

    /// <summary>Names the payload kind the walk moves on to, or nothing when it has reached the last of them.</summary>
    /// <remarks>
    /// The declared order rather than a list of its own, so a payload kind added later is walked without anything here
    /// being told about it. Which kind is first decides nothing but the order an operator watches the counters move in.
    /// </remarks>
    private static EmailContentKind? NextKindAfter(EmailContentKind kind) =>
        Enum.IsDefined(kind + 1) ? (EmailContentKind?)(kind + 1) : null;

    /// <summary>Reports whether one copy is the payload the row describes, in both length and digest.</summary>
    private static bool Matches(DatabaseBackedPayload payload, long byteLength, ReadOnlyMemory<byte> digest) =>
        payload.ByteLength == byteLength && payload.Sha256Hash.Span.SequenceEqual(digest.Span);

    /// <summary>Reports whether the operator has stopped the move since this pass read it, which ends the pass here.</summary>
    /// <remarks>
    /// One primary-key read of a single-row table between payloads, which is what makes pausing cost the message in
    /// flight rather than the rest of the pass — a ceiling of twenty payloads and sixty-four mebibytes is a long time to
    /// go on rewriting where somebody's mail is held after they asked for it to stop. It is deliberately not a read the
    /// walk trusts for anything else: a move replaced under the pass, or ended, is left for
    /// <see cref="RecordAsync" /> to recognize on the same terms it always did.
    /// <para>
    /// Outside the pass's cancellation, so that the one await between two payloads cannot itself raise. A shutdown
    /// landing here would otherwise end the pass through an exception rather than through the loop's own condition,
    /// which is what this type documents and what a caller reading that contract is entitled to. The read is a
    /// single-row primary-key lookup, so waiting for it costs a shutdown nothing worth measuring.
    /// </para>
    /// </remarks>
    private async Task<bool> StoppedByOperatorAsync(StoredContentMoveRun began)
    {
        var current = await this.runStore.FindAsync(CancellationToken.None);

        return current is not { State: StoredContentMoveState.Running }
            || current.RequestedAt != began.RequestedAt;
    }

    /// <summary>Copies one payload into the bucket, verifies it, and points its row at the object.</summary>
    /// <remarks>
    /// Every path through this returns, which is what advances the walk past the payload however it turned out: a payload
    /// the move cannot carry must not stand in front of every payload behind it. A payload that stopped being the
    /// database's between the batch and the read is neither copied nor failed: nothing is wrong and there is nothing to
    /// do, so the walk simply steps past it. An exception is the one thing that does not advance it — a shutdown that
    /// interrupted a payload leaves the position on the one before, so the next pass carries it from the beginning
    /// rather than skipping a message nobody decided about.
    /// </remarks>
    private async Task CarryAsync(
        DatabaseBackedPayload payload,
        WalkState walk,
        IStoredContentMovePassScope pass,
        CancellationToken cancellationToken)
    {
        walk.CarriedPayloadCount++;
        walk.ReachedByteCount += payload.ByteLength;

        if (payload.ByteLength > this.memoryBudget.CapacityBytes)
        {
            walk.FailedPayloadCount++;
            pass.Failed(StoredContentMoveFailure.Oversized);

            return;
        }

        var placement = await this.PlaceAsync(payload, pass, walk, cancellationToken);

        if (placement is not { ObjectLocator: { } objectLocator })
        {
            return;
        }

        if (!await this.VerifyAsync(payload, objectLocator, pass, walk, cancellationToken))
        {
            return;
        }

        if (await this.contentStore.RepointAtObjectAsync(
            payload.Kind,
            payload.PayloadId,
            objectLocator,
            cancellationToken))
        {
            walk.CopiedPayloadCount++;
            walk.MovedByteCount += payload.ByteLength;
            pass.Copied(payload.ByteLength);
        }
    }

    /// <summary>Reads one payload and puts it in the bucket, having first checked it against its own row.</summary>
    /// <remarks>
    /// The stored bytes are checked before anything is written rather than after, which keeps a payload nobody can vouch
    /// for out of the bucket entirely. Reading it under the process-wide budget is what makes the move wait behind
    /// ordinary work instead of holding memory beside it.
    /// </remarks>
    private async Task<PlacedEmailContent?> PlaceAsync(
        DatabaseBackedPayload payload,
        IStoredContentMovePassScope pass,
        WalkState walk,
        CancellationToken cancellationToken)
    {
        using var reservation = await this.memoryBudget.ReserveAsync(payload.ByteLength, cancellationToken);

        var rawMime = await this.contentStore.FindPayloadAsync(payload.Kind, payload.PayloadId, cancellationToken);

        if (rawMime is not { IsEmpty: false } storedBytes)
        {
            return null;
        }

        if (!Matches(payload, storedBytes.Length, SHA256.HashData(storedBytes.Span)))
        {
            walk.FailedPayloadCount++;
            pass.Failed(StoredContentMoveFailure.SourceMismatch);

            return null;
        }

        return await this.objectBackend.PlaceAsync(payload.Kind, storedBytes, cancellationToken);
    }

    /// <summary>Reads the object back and reports whether it is the payload the row describes.</summary>
    /// <remarks>
    /// Read back rather than trusted, although the endpoint verified the checksum the put carried. What the row is about
    /// to point at is the only copy the deployment will read from afterwards, and the one question worth the second
    /// request is whether that copy is there and is the message.
    /// <para>
    /// The read is bounded by what the row records, so an endpoint answering with more than the payload it was given
    /// meets a ceiling rather than this process growing a buffer to fit somebody else's answer. What comes back is then
    /// longer than the row describes, which is the mismatch it is: the row is left exactly where it is.
    /// </para>
    /// </remarks>
    private async Task<bool> VerifyAsync(
        DatabaseBackedPayload payload,
        string objectLocator,
        IStoredContentMovePassScope pass,
        WalkState walk,
        CancellationToken cancellationToken)
    {
        using var reservation = await this.memoryBudget.ReserveAsync(payload.ByteLength, cancellationToken);

        var written = await this.objectBackend.ReadBackAsync(objectLocator, payload.ByteLength, cancellationToken);

        if (written is not { } writtenBytes)
        {
            walk.FailedPayloadCount++;
            pass.Failed(StoredContentMoveFailure.ObjectAbsent);

            return false;
        }

        if (!Matches(payload, writtenBytes.Length, SHA256.HashData(writtenBytes.Span)))
        {
            walk.FailedPayloadCount++;
            pass.Failed(StoredContentMoveFailure.ObjectMismatch);

            return false;
        }

        return true;
    }

    /// <summary>Commits what this pass carried, and where it got to, onto the move an operator is watching.</summary>
    /// <remarks>
    /// From a fresh read inside the commit, because an operator can pause the move while a pass is running: the counts
    /// and the position are this pass's to write and the state is theirs, so writing the record this pass began with
    /// would start the move again on their behalf. A move that ended or was replaced under the pass is left alone
    /// entirely.
    /// <para>
    /// Outside the pass's cancellation, deliberately: the one moment progress most needs to be written down is the
    /// shutdown that stopped the pass, and a write cancelled by the token that stopped it would leave everything this
    /// pass repointed to be walked again.
    /// </para>
    /// </remarks>
    private Task RecordAsync(StoredContentMoveRun began, WalkState walk) =>
        this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.runStore.FindAsync(attemptCancellationToken) is not { } current
                    || current.RequestedAt != began.RequestedAt
                    || current.State is StoredContentMoveState.Completed)
                {
                    return;
                }

                await this.runStore.SaveAsync(
                    session,
                    current with
                    {
                        State = walk.ReachedEnd ? StoredContentMoveState.Completed : current.State,
                        Kind = walk.Kind,
                        ResumeAfter = walk.ResumeAfter,
                        CopiedPayloadCount = current.CopiedPayloadCount + walk.CopiedPayloadCount,
                        FailedPayloadCount = current.FailedPayloadCount + walk.FailedPayloadCount,
                        MovedByteCount = current.MovedByteCount + walk.MovedByteCount,
                        EndedAt = walk.ReachedEnd ? this.timeProvider.GetUtcNow() : current.EndedAt,
                    },
                    attemptCancellationToken);
            },
            CancellationToken.None);

    /// <summary>Where one pass has got to, which the pass accumulates and commits once.</summary>
    /// <remarks>
    /// Mutable and local to one invocation. The alternative is threading eight values through five methods, and the
    /// values are one thing — a pass in progress — rather than eight.
    /// </remarks>
    private sealed class WalkState(EmailContentKind kind, Guid? resumeAfter)
    {
        public EmailContentKind Kind { get; set; } = kind;

        public Guid? ResumeAfter { get; set; } = resumeAfter;

        /// <summary>Gets or sets how many payloads the pass has reached, whether or not each of them moved.</summary>
        /// <remarks>What the pass is bounded by, rather than the copied count: a pass that met twenty payloads it could not carry has done twenty payloads' worth of work.</remarks>
        public int CarriedPayloadCount { get; set; }

        /// <summary>Gets or sets the declared length of every payload the pass has reached, whether or not its bytes were ever read.</summary>
        /// <remarks>The byte ceiling's counterpart to <see cref="CarriedPayloadCount" />, and counted the same way: a payload refused for being larger than this process may hold cost the pass its whole size in what the pass was willing to take on.</remarks>
        public long ReachedByteCount { get; set; }

        public long CopiedPayloadCount { get; set; }

        public long FailedPayloadCount { get; set; }

        public long MovedByteCount { get; set; }

        public bool ReachedEnd { get; set; }
    }
}
