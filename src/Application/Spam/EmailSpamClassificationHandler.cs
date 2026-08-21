// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>Runs the classification of one message occurrence as a leased execution of the durable queue.</summary>
/// <remarks>
/// <para>
/// The work is the use case that already exists, reached once per occurrence: the queue supplies the lease, the bounded
/// attempts, the jittered backoff between them, and the dead letter that ends a job nothing can finish. That is the
/// whole reason classification is a job rather than a step of the account's synchronization run — a scan reaches a
/// sidecar that can be unreachable, saturated, or restarting, and a run that deferred the whole account could not
/// express one message out of three hundred deserving another attempt.
/// </para>
/// <para>
/// Nothing here is a scheduler, a retry policy, or an idempotency identity of its own. The classification record carries
/// no attempt count and no next-attempt time, because those are the queue row's and putting them on the record would be
/// a job written in the wrong place.
/// </para>
/// <para>
/// Running it twice with one payload is the same as running it once, which is what the queue asks of every handler. The
/// use case is keyed to the occurrence and asked to leave an existing record alone, and the changes a verdict asks of
/// the mailbox are written under an identity of their own, so an attempt that crashed after committing its verdict
/// leaves the next one with the same verdict to act on rather than with a second one to reach.
/// </para>
/// <para>
/// It reaches no mail server for the message it reads. Content comes from the local content store, so no classification
/// path opens an IMAP session and none can affect a remote <c>\Seen</c> flag; the one thing that does reach the mailbox
/// is a change an operator switched on, and that is written down as a durable mutation record for the account's own
/// convergence pass to carry.
/// </para>
/// </remarks>
public sealed class EmailSpamClassificationHandler : IJobHandler
{
    private readonly IClassifiableEmailReader emails;
    private readonly IEmailSpamClassificationStore classifications;
    private readonly EmailSpamClassifier classifier;
    private readonly SpamActionRecorder actionRecorder;

    /// <summary>Initializes the handler over the work one job describes.</summary>
    /// <param name="emails">Turns the occurrence the payload names into the local email it was stored as.</param>
    /// <param name="classifications">Reads the verdict an earlier attempt recorded, so a repeated attempt still acts on it.</param>
    /// <param name="classifier">Reaches a verdict about the occurrence and records it.</param>
    /// <param name="actionRecorder">Writes down whatever the operator's switches ask a verdict to do to the mailbox.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public EmailSpamClassificationHandler(
        IClassifiableEmailReader emails,
        IEmailSpamClassificationStore classifications,
        EmailSpamClassifier classifier,
        SpamActionRecorder actionRecorder)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(actionRecorder);

        this.emails = emails;
        this.classifications = classifications;
        this.classifier = classifier;
        this.actionRecorder = actionRecorder;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.ClassifyEmailSpam;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    /// <remarks>
    /// An occurrence nothing is stored at ends the job as done rather than as a failure. Mail can be expunged between
    /// the moment a classification was asked for and the moment it runs, and that is the message leaving rather than
    /// work to attempt again.
    /// </remarks>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not ClassifyEmailSpamJobPayload occurrence)
        {
            throw new ArgumentException(
                $"A '{JobType.ClassifyEmailSpam}' job carries a payload naming one message occurrence.",
                nameof(payload));
        }

        var storedEmailId = await this.emails.FindStoredEmailIdAsync(
            occurrence.ToOccurrenceId(),
            cancellationToken);

        if (storedEmailId is not { } emailId)
        {
            return;
        }

        var classification = await this.ClassifyAsync(emailId, cancellationToken);

        if (classification is not null)
        {
            await this.actionRecorder.RecordAsync(classification, SpamActionPosture.Acting, cancellationToken);
        }
    }

    /// <summary>Reaches a verdict about the occurrence, or recovers the one an earlier attempt already recorded.</summary>
    /// <remarks>
    /// The re-read is what makes a repeated attempt whole rather than merely harmless. An attempt that committed its
    /// verdict and then lost its lease left the message classified and its filing unasked for, and a second attempt that
    /// read only its own result would see an occurrence already classified and end having done neither. Every other
    /// outcome is a reason no verdict exists — classification switched off, a folder outside the scope, content that is
    /// not stored — and none of them has anything for the mailbox to be asked about.
    /// </remarks>
    private async Task<SpamClassification?> ClassifyAsync(
        StoredEmailId emailId,
        CancellationToken cancellationToken)
    {
        var result = await this.classifier.ClassifyAsync(
            emailId,
            SpamClassificationMode.FirstTimeOnly,
            cancellationToken);

        return result.Outcome is SpamClassificationOutcome.AlreadyClassified
            ? await this.classifications.FindAsync(emailId, cancellationToken)
            : result.Classification;
    }
}
