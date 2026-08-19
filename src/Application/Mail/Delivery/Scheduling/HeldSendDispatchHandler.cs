// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Mail.Delivery.Outbox;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>Tells an account's outbox that a message it has been holding is now due to leave.</summary>
/// <remarks>
/// <para>
/// The job is deliberately short, and it transmits nothing. The message was written down when it was authored and has
/// been unclaimable ever since, because the instant it may next be claimed at is the instant it was written to leave
/// at; what was missing was somebody to notice that instant arriving. That is what the durable queue already does for
/// every other kind of deferred work, so a held send needs a job rather than a timer, a scheduler, or a queue of its
/// own.
/// </para>
/// <para>
/// A send that was cancelled while it was held, or one an earlier pass has already taken, is not signalled. The pass
/// would find nothing to claim either way, so this is about what an operator reads rather than about correctness: a job
/// that signalled an account for a message nobody is waiting for would report work that was not there.
/// </para>
/// <para>
/// Running it twice with one payload is the same as running it once. A signal is a request for a pass over an account,
/// the pass reads the outbox rather than this job, and an account already waiting for one is not queued twice.
/// </para>
/// <para>
/// Nothing here decides whether the message is still timely. That belongs to the pass, which holds the lease the
/// decision has to be recorded under, and which reaches the same decision for a message this job never ran for —
/// because the deployment was down when it came due, or because the queue was full when it was written.
/// </para>
/// </remarks>
public sealed class HeldSendDispatchHandler : IJobHandler
{
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly MailOutboxSignal signal;

    /// <summary>Initializes the handler from the record it reads and the loop it wakes.</summary>
    /// <param name="outgoingEmails">Answers whether the message is still waiting to be sent.</param>
    /// <param name="signal">Tells the delivery loop that this account has something to send.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public HeldSendDispatchHandler(IOutgoingEmailStore outgoingEmails, MailOutboxSignal signal)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(signal);

        this.outgoingEmails = outgoingEmails;
        this.signal = signal;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.DispatchHeldSend;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not HeldSendJobPayload heldSend)
        {
            throw new ArgumentException(
                $"A '{JobType.DispatchHeldSend}' job carries a payload naming one held send.",
                nameof(payload));
        }

        var record = await this.outgoingEmails.FindAsync(heldSend.ToOutgoingEmailId(), cancellationToken);

        if (record is null || record.IsTerminal)
        {
            return;
        }

        this.signal.Signal(heldSend.ToAccountId());
    }
}
