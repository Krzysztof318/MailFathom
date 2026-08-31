// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam;

/// <summary>Asks for a classification of each message synchronization has just committed.</summary>
/// <remarks>
/// <para>
/// This is the whole arrival trigger. It is reached once per stored message, after the transaction that stored the
/// message and its content has committed, and it writes one row into the durable queue — where the work is leased,
/// retried per message, and made idempotent by the queue's own model rather than by anything here.
/// </para>
/// <para>
/// A message stored without its content is deliberately not asked for. Classification reads the local content store and
/// reports a message it cannot read as unclassifiable rather than fetching it, so enqueuing one would spend a lease to
/// reach an answer that is already known — and the gate over derived work already releases such a message rather than
/// holding it.
/// </para>
/// <para>
/// The two cheap questions are asked before the row is written, in the order they cost: whether this owner classifies
/// at all, and whether their scope covers the folder the message arrived in. An owner with classification off therefore
/// costs one settings read per stored message and reaches no queue, which is the same shape every other path through
/// this feature has when it is switched off.
/// </para>
/// <para>
/// <strong>What the queue answers is not acted on, and that is the bound the synchronization run needs.</strong> A row
/// this call wrote, a row an earlier attempt already wrote, and a refusal because the queue holds as much of this type
/// as the deployment accepts are all the same thing here: the message is stored, and a classification either happens or
/// the wait a verdict is allowed releases the message to derived work without one. A refusal is visible as the queue
/// depth standing at its configured bound, which is where backpressure is read from rather than from anything this type
/// records.
/// </para>
/// <para>
/// Nothing from the message reaches the queue. The payload names the occurrence and the idempotency key is the message's
/// own stored identity, so an operator reading a stuck job sees where the message is and can ask what was concluded
/// about it, and neither carries a subject, an address, or anything else out of the mail.
/// </para>
/// </remarks>
public sealed class SpamClassificationArrivals
{
    private readonly IJobStore jobs;
    private readonly ISpamClassificationSettingsReader settingsReader;

    /// <summary>Initializes the trigger over the queue it writes to and the settings that decide whether it does.</summary>
    /// <param name="jobs">The durable queue one classification is enqueued into.</param>
    /// <param name="settingsReader">Answers whether the occurrence's owner classifies and which folders they cover.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SpamClassificationArrivals(IJobStore jobs, ISpamClassificationSettingsReader settingsReader)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(settingsReader);

        this.jobs = jobs;
        this.settingsReader = settingsReader;
    }

    /// <summary>Asks for one committed message to be classified.</summary>
    /// <param name="emailId">The local identity the occurrence was stored as, which the execution is keyed by.</param>
    /// <param name="occurrenceId">The occurrence synchronization has just stored, with its content.</param>
    /// <param name="owner">The owner the run resolved the account under, which the queued work is recorded against.</param>
    /// <param name="cancellationToken">Cancels the enqueue.</param>
    /// <returns>A task that completes once the queue has answered, or at once where no classification is wanted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrenceId" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Asking twice for one message is asking once. Both identities name the same message — the local row is keyed by
    /// the occurrence — so a run that stored it again, whether a folder walked afresh or a run resumed after a crash, is
    /// answered with the job that is already there rather than adding a second one.
    /// </remarks>
    public async Task ScheduleAsync(
        StoredEmailId emailId,
        EmailOccurrenceId occurrenceId,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

        var settings = this.settingsReader.SettingsFor(owner);

        if (!settings.IsEnabled || !settings.Covers(occurrenceId.FolderResolutionId.Alias))
        {
            return;
        }

        var request = JobEnqueueRequest.Create(
            KeyOf(emailId),
            ClassifyEmailSpamJobPayload.For(owner, occurrenceId),

            // Composed from the owner the run already resolved rather than looked up here: the queue row records whose
            // account the classification is about, and the synchronization run settled that once for the whole run.
            MailAccountIdentity.Create(owner, occurrenceId.AccountId));

        await this.jobs.EnqueueAsync(request, cancellationToken);
    }

    /// <summary>Composes the identity two enqueues of one message's classification are compared by.</summary>
    /// <remarks>
    /// The stored identity rather than the occurrence written out, and the reason is a bound rather than a preference: a
    /// key may be 256 characters, while an account identifier and a folder alias may each be 128, so a composition of
    /// the two plus the remote numbers can exceed it — and a key that cannot be composed would raise out of the
    /// synchronization run that asked, which is exactly what this trigger may never do. What is lost is nothing an
    /// operator needs: the occurrence is in the payload beside the key, and this is the identifier
    /// <c>mfctl spam classifications --email</c> already takes, so a stuck job leads straight to what was concluded.
    /// </remarks>
    private static JobIdempotencyKey KeyOf(StoredEmailId emailId) => JobIdempotencyKey.Create(
        emailId.Value.ToString("d", CultureInfo.InvariantCulture));
}
