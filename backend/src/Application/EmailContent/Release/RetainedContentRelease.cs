// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Release;

/// <summary>Frees the database copies the move left beside the objects it verified, one bounded batch per request.</summary>
/// <remarks>
/// <para>
/// The move copies and never removes. A payload it has carried is read from its object and still holds the bytes the
/// database always held, which is what lets a read fall back while a deployment is trusting its bucket for the first
/// time. This is the step that ends that, and it is the only irreversible thing the whole move does: what it removes is
/// the last copy of a message outside the endpoint.
/// </para>
/// <para>
/// So it is an operator's request each time rather than a consequence of anything. No interval elapsing performs it, no
/// worker carries it, and a move reaching the end of the content does not begin it — because a background job that
/// disposed of mail on a timer is precisely what an operator must not have to trust.
/// </para>
/// <para>
/// <b>It refuses outright while the database still owns a payload.</b> A payload the move has not carried is one no
/// object was ever verified for, and a deployment holding one is a deployment whose move is unfinished; freeing the
/// copies of everything else would end the safety of a job somebody is still in the middle of. The refusal names the
/// backlog, and another move is what repairs it.
/// </para>
/// <para>
/// Nothing here reads a payload or reaches the endpoint. The object was read back and checked against the row's own
/// length and digest before the row was ever pointed at it, that check is what the row records, and those two values
/// stay on the row afterwards so the object is still checkable once the bytes are gone.
/// </para>
/// </remarks>
public sealed class RetainedContentRelease
{
    private readonly IRetainedContentReleaseStore releaseStore;
    private readonly IStoredContentMoveStore contentStore;
    private readonly IRetainedContentReleaseTelemetry telemetry;
    private readonly RetainedContentReleaseOptions options;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the release.</summary>
    /// <param name="releaseStore">Counts what is retained, and frees a bounded batch of it.</param>
    /// <param name="contentStore">Counts what the database still owns, which is what a release is refused for.</param>
    /// <param name="telemetry">Publishes what was freed.</param>
    /// <param name="options">Bounds one batch, and holds a copy for the configured safety interval.</param>
    /// <param name="timeProvider">Turns the safety interval into the cutoff a batch is selected by.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public RetainedContentRelease(
        IRetainedContentReleaseStore releaseStore,
        IStoredContentMoveStore contentStore,
        IRetainedContentReleaseTelemetry telemetry,
        RetainedContentReleaseOptions options,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(releaseStore);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.releaseStore = releaseStore;
        this.contentStore = contentStore;
        this.telemetry = telemetry;
        this.options = options;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
    }

    /// <summary>Reads what is retained and what is still uncarried, without freeing anything.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The two figures, with nothing released.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>
    /// Asked for under the reading grant rather than the erasing one, because how much of a database is duplication is a
    /// question about what a deployment holds. It answers on a deployment that has moved nothing, where both figures are
    /// what they should be: nothing retained, and everything still owned.
    /// </remarks>
    public async Task<RetainedContentReleaseResult> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return new RetainedContentReleaseResult(
            ReleasedContentPayloads.None,
            await this.releaseStore.CountRetainedPayloadsAsync(cancellationToken),
            await this.contentStore.CountPayloadsAwaitingMoveAsync(cancellationToken));
    }

    /// <summary>Frees one bounded batch of the retained copies, if the deployment has finished carrying its content.</summary>
    /// <param name="cancellationToken">Cancels the release between payload kinds, leaving what earlier batches freed.</param>
    /// <returns>What this request freed, what is retained behind it, and the backlog when it was refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// The grant is the one this deployment allocates to disposing of what it holds rather than the one it allocates to
    /// work, because that is what this is: a credential that may ask for the copy must not be able to end it.
    /// </para>
    /// <para>
    /// One batch spans the payload kinds in the order they are declared, spending what the bound allows on the first
    /// kind that has copies to free and carrying the remainder to the next. A kind whose copies are all still inside the
    /// safety interval frees nothing and costs the batch nothing, so the interval holds a kind back without stopping the
    /// release of the rest.
    /// </para>
    /// </remarks>
    public async Task<RetainedContentReleaseResult> ReleaseAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminErase);

        var awaitingMove = await this.contentStore.CountPayloadsAwaitingMoveAsync(cancellationToken);

        if (awaitingMove.PayloadCount > 0)
        {
            return new RetainedContentReleaseResult(
                ReleasedContentPayloads.None,
                await this.releaseStore.CountRetainedPayloadsAsync(cancellationToken),
                awaitingMove);
        }

        var released = await this.ReleaseBatchAsync(cancellationToken);

        return new RetainedContentReleaseResult(
            released,
            await this.releaseStore.CountRetainedPayloadsAsync(cancellationToken),
            StoredContentBacklog.Empty);
    }

    /// <summary>Spends one batch's bound across the payload kinds, in the order they are declared.</summary>
    /// <remarks>
    /// <para>
    /// The declared order rather than a list of its own, so a payload kind added later is released without anything here
    /// being told about it. Which kind is first decides nothing but the order an operator watches the figures move in.
    /// </para>
    /// <para>
    /// Each kind is published as it is freed rather than the batch being published at the end, because the removal is
    /// already durable by then and a cancellation between two kinds must not be the reason a deployment has no record of
    /// what it disposed of. Both instruments are counters, so a batch spanning three kinds sums to exactly what one
    /// measurement would have said.
    /// </para>
    /// </remarks>
    private async Task<ReleasedContentPayloads> ReleaseBatchAsync(CancellationToken cancellationToken)
    {
        var cutoff = this.timeProvider.GetUtcNow() - this.options.SafetyInterval;
        var released = ReleasedContentPayloads.None;
        var remainingBound = this.options.PayloadsPerBatch;

        foreach (var kind in Enum.GetValues<EmailContentKind>())
        {
            if (remainingBound <= 0)
            {
                break;
            }

            var freed = await this.releaseStore.ReleaseAsync(kind, cutoff, remainingBound, cancellationToken);

            if (freed.PayloadCount > 0)
            {
                this.telemetry.Released(freed.PayloadCount, freed.ByteCount);
            }

            released = released.Add(freed);
            remainingBound -= (int)freed.PayloadCount;
        }

        return released;
    }
}
