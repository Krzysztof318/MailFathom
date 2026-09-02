// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>Keeps every declaration that a message is sent again on the occasions a schedule names.</summary>
/// <remarks>
/// <para>
/// A declaration is state rather than settings, which is why it is here and not in the deployment's configuration. What
/// repeats is a message somebody wrote, at a moment they chose, and it is stopped by them as well — none of which an
/// operator's file can hold, and all of which the database can.
/// </para>
/// <para>
/// The idempotency identity is the sending account and the authoring act together, exactly as an outgoing record's is,
/// and it is enforced by a unique constraint rather than by this contract declining to write. A caller that retried a
/// command reads back the declaration it already made instead of declaring a second one that would send everything
/// twice.
/// </para>
/// <para>
/// Writes take the caller's session, because a declaration and the draft its occasions are composed from are one write:
/// a declaration whose draft was never stored describes occasions that can produce no message, and a draft under no
/// declaration is bytes nothing will ever read.
/// </para>
/// </remarks>
public interface IRecurringSendStore
{
    /// <summary>Writes the declaration down, or reads back the one that already holds this idempotency identity.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="request">The repetition that was asked for.</param>
    /// <param name="draftByteLength">How many bytes of MIME are being stored as the draft.</param>
    /// <param name="cancellationToken">Cancels the write or the read that precedes it.</param>
    /// <returns>The declaration for this request, whether this call created it or an earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="draftByteLength" /> is not positive.</exception>
    /// <remarks>
    /// The declaration starts active and having produced nothing, so writing one sends nothing by itself. An identity
    /// that already has a declaration is answered with that declaration unchanged, including the schedule it was made
    /// with: a second call that meant to change the repetition has to stop this one and declare afresh, because a
    /// schedule edited underneath a running declaration would leave the occasions it already accounted for describing a
    /// repetition that no longer exists.
    /// </remarks>
    Task<RecurringSend> DeclareAsync(
        IPersistenceSession session,
        RecurringSendRequest request,
        long draftByteLength,
        CancellationToken cancellationToken);

    /// <summary>Reads one declaration back by the identifier its occasions and its cancellation name it by.</summary>
    /// <param name="recurringSendId">The declaration to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The declaration, or <see langword="null" /> when none carries that identifier.</returns>
    Task<RecurringSend?> FindAsync(RecurringSendId recurringSendId, CancellationToken cancellationToken);

    /// <summary>Reads what the dispatch needs about the declarations that still produce occurrences.</summary>
    /// <param name="limit">The greatest number of declarations to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The active declarations, oldest first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// This is what the recurring dispatch reads on every pass, so the bound is what stops a deployment that declared
    /// more repetitions than a pass can carry from making that pass unbounded. It is a page rather than a cut only in
    /// the sense every other bounded query is: what a pass does not reach this time it reaches on the next one. What it
    /// answers with is the projection rather than the declaration, so a decision taken every interval never loads the
    /// addresses it does not read.
    /// </remarks>
    Task<IReadOnlyList<RecurringSendDeclaration>> ReadActiveAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stops a declaration from producing any further occurrence.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recurringSendId">The declaration to stop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It stops occasions and touches no message. An occurrence already written down goes out as it was going to,
    /// because it is a message the owner asked for at a moment that has already come; stopping that one as well is the
    /// other act, asked for against the record it produced.
    /// </remarks>
    Task<RecurringSendCancellation> CancelAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken);

    /// <summary>Records the occasion a declaration has now produced a message for.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recurringSendId">The declaration the occasion belongs to.</param>
    /// <param name="occurrenceAt">The occasion itself, rather than the moment it was noticed.</param>
    /// <param name="outgoingEmailId">The message that occasion produced.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the occasion is durable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no declaration carries <paramref name="recurringSendId" />.</exception>
    /// <remarks>
    /// An occasion already recorded is not moved backwards. Two instances reaching one occasion write the same values,
    /// and a dispatch that ran late must not take a declaration back to an occasion a later one already passed.
    /// </remarks>
    Task RecordOccurrenceAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        DateTimeOffset occurrenceAt,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);
}
