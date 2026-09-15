// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs;

/// <summary>States one execution to be done later, in terms of state that is already committed.</summary>
/// <remarks>
/// <para>
/// The request accepts the identity of work over stored state and nothing an open synchronization transaction is still
/// holding. That is the shape of the contract rather than a rule to remember: there is no property here to hand it a
/// message it has not committed.
/// </para>
/// <para>
/// The job type is read from the payload rather than supplied beside it, because a type names exactly one payload
/// contract and two values would make disagreeing about it possible.
/// </para>
/// </remarks>
public sealed record JobEnqueueRequest
{
    private JobEnqueueRequest(
        JobIdempotencyKey key,
        IJobPayload payload,
        MailAccountId? account,
        DateTimeOffset? availableAt)
    {
        ArgumentNullException.ThrowIfNull(key);

        this.Key = key;
        this.Payload = payload;
        this.Account = account;
        this.AvailableAt = availableAt;
    }

    /// <summary>Gets the identity that decides whether this is a new execution or one already enqueued.</summary>
    public JobIdempotencyKey Key { get; }

    /// <summary>Gets the references the work is described by.</summary>
    public IJobPayload Payload { get; }

    /// <summary>Gets the type of work, which is the one the payload is the contract of.</summary>
    public JobType JobType => this.Payload.JobType;

    /// <summary>Gets the account the work belongs to, by its generated identifier, or <see langword="null" /> when it belongs to none.</summary>
    /// <remarks>
    /// <para>
    /// It becomes a column of the row rather than a value inside the document, because erasure, retention, and any
    /// per-account bound have to reach a job by query rather than by searching inside a document.
    /// </para>
    /// <para>
    /// The identifier alone is the whole of it: it names one mailbox across the deployment, so nothing has to be
    /// looked up or joined to say which mailbox a job is for, and the fair turn between mailboxes is measured on that
    /// same column. Which people the mailbox serves is the assignment relation's answer and is read when something
    /// actually needs a person, never written here — a job is work on a mailbox rather than work for somebody.
    /// </para>
    /// <para>
    /// Absence covers work no mailbox asked for, which is the deployment's own housekeeping.
    /// </para>
    /// </remarks>
    public MailAccountId? Account { get; }

    /// <summary>Gets the instant before which the job is not claimable, or <see langword="null" /> to make it claimable at once.</summary>
    public DateTimeOffset? AvailableAt { get; }

    /// <summary>States an execution to be done as soon as a worker can take it.</summary>
    /// <param name="key">The identity of the execution, composed by whoever knows the work.</param>
    /// <param name="payload">The references the work is described by.</param>
    /// <param name="account">The account the work belongs to, by its generated identifier, or <see langword="null" /> when it belongs to none.</param>
    /// <returns>The request to enqueue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key" /> or <paramref name="payload" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the payload names no declared job type.</exception>
    public static JobEnqueueRequest Create(JobIdempotencyKey key, IJobPayload payload, MailAccountId? account) =>
        new(key, ValidPayload(payload), account, availableAt: null);

    /// <summary>States an execution to be done no earlier than a given instant.</summary>
    /// <param name="key">The identity of the execution, composed by whoever knows the work.</param>
    /// <param name="payload">The references the work is described by.</param>
    /// <param name="account">The account the work belongs to, by its generated identifier, or <see langword="null" /> when it belongs to none.</param>
    /// <param name="availableAt">The instant before which no worker may claim the job.</param>
    /// <returns>The request to enqueue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key" /> or <paramref name="payload" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the payload names no declared job type.</exception>
    /// <remarks>An instant already past is accepted and means the same as none: the claim compares the column against the database's clock, so a job is never made unclaimable by having been scheduled late.</remarks>
    public static JobEnqueueRequest CreateAvailableAt(
        JobIdempotencyKey key,
        IJobPayload payload,
        MailAccountId? account,
        DateTimeOffset availableAt) => new(key, ValidPayload(payload), account, availableAt);

    private static IJobPayload ValidPayload(IJobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.JobType.IsSpecified)
        {
            throw new ArgumentException("A job payload names a declared job type.", nameof(payload));
        }

        return payload;
    }
}
