// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;

namespace MailFathom.Application.Spam;

/// <summary>Runs a classification that names a remote occurrence, by resolving it to the stored email it names.</summary>
/// <remarks>
/// <para>
/// Nothing this build enqueues reaches it: arriving mail is asked for under <see cref="JobType.ClassifyStoredEmailSpam" />.
/// It is here because a rolling upgrade runs two builds against one queue, and a replica of the previous build goes on
/// enqueuing <see cref="JobType.ClassifyEmailSpam" /> until it is replaced. A type nothing handled would leave those rows
/// waiting for a replica that no longer exists, and a new document shape under the old name would stop every batch an
/// older replica claimed.
/// </para>
/// <para>
/// The occurrence is resolved under the payload's user, so an occurrence that names another user's mail resolves to
/// nothing. A stored email no server holds any longer has no occurrence to be found at, and ends the job as done for the
/// reason a message expunged before the job ran always has. Whatever it resolves to is handed to the classification of a
/// stored email, so both types run one use case.
/// </para>
/// </remarks>
public sealed class OccurrenceSpamClassificationHandler : IJobHandler
{
    private readonly IClassifiableEmailReader emails;
    private readonly EmailSpamClassificationHandler storedEmailClassification;

    /// <summary>Initializes the handler over the resolution and the classification it hands the result to.</summary>
    /// <param name="emails">Turns the occurrence the payload names into the stored email it was stored as.</param>
    /// <param name="storedEmailClassification">Classifies the stored email and acts on the verdict.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public OccurrenceSpamClassificationHandler(
        IClassifiableEmailReader emails,
        EmailSpamClassificationHandler storedEmailClassification)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(storedEmailClassification);

        this.emails = emails;
        this.storedEmailClassification = storedEmailClassification;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.ClassifyEmailSpam;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not ClassifyEmailSpamJobPayload occurrence)
        {
            throw new ArgumentException(
                $"A '{JobType.ClassifyEmailSpam}' job carries a payload naming one message occurrence.",
                nameof(payload));
        }

        var account = occurrence.ToAccountIdentity();
        var storedEmailId = await this.emails.FindStoredEmailIdAsync(
            account.User,
            occurrence.ToOccurrenceId(),
            cancellationToken);

        if (storedEmailId is not { } emailId)
        {
            return;
        }

        await this.storedEmailClassification.RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(account, emailId),
            cancellationToken);
    }
}
