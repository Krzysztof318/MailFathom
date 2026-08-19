// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Puts a copy of an outgoing message into the folder the message's own state calls for.</summary>
/// <remarks>
/// <para>
/// One mechanism for every place a message MailFathom authored belongs: a draft in the drafts folder, a message still
/// waiting in the folder an operator mapped as the outbox, a delivered message in the sent folder. What differs between
/// them is the role of the destination and the flags the copy carries, and both travel on
/// <see cref="OutgoingMailFiling" /> — so a fourth place is a member there rather than a second filer here.
/// </para>
/// <para>
/// The destination is found by role and never by name, because every one of these folders is spelled differently in
/// every locale and by every provider. A role no folder of the account plays is a destination that is not available,
/// which is the same answer a mapped folder the server does not advertise gives: the account's folder mapping is what
/// changes it, and the message is untouched either way.
/// </para>
/// <para>
/// The order of the two writes is the whole of the safety here. An <c>APPEND</c> issued twice is a second message in
/// somebody's folder rather than a repeat of the first, and nothing that folder shows afterwards tells the two apart —
/// so the row is durable at <see cref="OutgoingMailFilingStage.Issued" /> before the command goes out, and a row found
/// there is reported rather than appended again. Everything that can fail without reaching the server happens before
/// that write, which is what keeps an ordinary connection failure retryable.
/// </para>
/// <para>
/// Nothing here can fail a delivery. A send whose copy could not be filed is a send that happened, so the failure is
/// written beside the delivery stage rather than over it, and no delivery is ever attempted again because of one.
/// </para>
/// </remarks>
public sealed class OutgoingMailFiler
{
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly MailboxDestinationResolver destinations;
    private readonly IEmailContentStore contentStore;
    private readonly IOutgoingMailFilingStore filings;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the filer from the session it appends through and the record it writes the copy onto.</summary>
    /// <param name="writeSessions">Opens the one session able to change a mailbox.</param>
    /// <param name="destinations">Turns the role a filing names into the folder of this account it means.</param>
    /// <param name="contentStore">Holds the stored MIME the copy is appended from.</param>
    /// <param name="filings">Keeps the durable account of every copy that has been filed.</param>
    /// <param name="transportSecurityPolicies">Supplies the connection and authentication policy the append obeys.</param>
    /// <param name="commitPolicy">Commits each movement of the filing row.</param>
    /// <param name="timeProvider">Stamps the copy's internal date and everything the row records.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public OutgoingMailFiler(
        IMailboxWriteSessionFactory writeSessions,
        MailboxDestinationResolver destinations,
        IEmailContentStore contentStore,
        IOutgoingMailFilingStore filings,
        IMailTransportSecurityPolicyReader transportSecurityPolicies,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicies);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.writeSessions = writeSessions;
        this.destinations = destinations;
        this.contentStore = contentStore;
        this.filings = filings;
        this.transportSecurityPolicies = transportSecurityPolicies;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Files one copy of a message into the place its state calls for.</summary>
    /// <param name="record">The outgoing record the copy is filed from, read with its existing filings.</param>
    /// <param name="filing">Which place the copy goes into.</param>
    /// <param name="cancellationToken">Cancels the append and the writes around it.</param>
    /// <returns>What the attempt did, which is already durable by the time it is returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filing" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// It does not raise for a copy that could not be filed. Everything the attempt learned is written onto the record
    /// first, so a caller reads the outcome rather than catching it — which is what lets a filing failure leave the
    /// delivery that preceded it exactly as it was.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A filing failure must never reach the delivery that preceded it, because a send whose copy could not be filed is a send that happened; the failure is classified into a recorded code and returned as an outcome rather than raised.")]
    public async Task<OutgoingMailFilingResult> FileAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        RequireSpecified(filing);

        if (SettledOutcomeOf(record, filing) is { } settled)
        {
            return Result(record, filing, settled, failure: null);
        }

        try
        {
            return await this.AppendAsync(record, filing, cancellationToken);
        }
        catch (Exception failure)
        {
            // Nothing reached the mail server: everything past the issued write is caught where it happens, and the
            // row this would have been written against was never opened.
            return await this.RecordFailureAsync(
                record,
                filing,
                FailureCodeOf(failure),
                OutgoingMailFilingOutcome.Failed);
        }
    }

    /// <summary>Takes a copy back out of the folder it was filed into.</summary>
    /// <param name="record">The outgoing record the copy was filed from, read with its existing filings.</param>
    /// <param name="filing">Which place the copy was in.</param>
    /// <param name="cancellationToken">Cancels the withdrawal and the write that records it.</param>
    /// <returns>What the attempt did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filing" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// <para>
    /// A copy the server never named cannot be reached, because nothing identifies it: the alternative would be
    /// searching the folder for something that looks like the message, which is a guess about identity rather than a
    /// fact. Such a row is marked withdrawn all the same, so nothing tries forever — what is left behind is one copy of
    /// the owner's own message in a folder they mapped, which they delete with the gesture they would have used anyway.
    /// </para>
    /// <para>
    /// A folder that no longer holds the copy is not a failure. The owner deleting it themselves is the ordinary case,
    /// and what the withdrawal asked for is already true.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A copy that could not be withdrawn is one message left in a folder the operator mapped; raising would end the pass that was settling the send it belongs to, over a failure that says nothing about the send.")]
    public async Task<OutgoingMailFilingResult> WithdrawAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        RequireSpecified(filing);

        if (record.FindFiling(filing) is not { IsStanding: true } standing)
        {
            return Result(record, filing, OutgoingMailFilingOutcome.NotRequested, failure: null);
        }

        try
        {
            if (standing is
                {
                    Stage: OutgoingMailFilingStage.Confirmed,
                    Placement: { UidValidity: { } uidValidity, Uid: { } uid },
                })
            {
                await this.WithdrawFromServerAsync(record, standing, uidValidity, uid, cancellationToken);
            }

            await this.commitPolicy.CommitAsync(
                (session, token) => this.filings.RecordWithdrawnAsync(
                    session,
                    record.Id,
                    filing,
                    this.timeProvider.GetUtcNow(),
                    token),
                CancellationToken.None);

            return Result(record, filing, OutgoingMailFilingOutcome.Withdrawn, failure: null);
        }
        catch (Exception failure)
        {
            return await this.RecordFailureAsync(
                record,
                filing,
                FailureCodeOf(failure),
                OutgoingMailFilingOutcome.Failed);
        }
    }

    private static void RequireSpecified(OutgoingMailFiling filing)
    {
        if (!filing.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct names no filing.", nameof(filing));
        }
    }

    /// <summary>Answers from the record alone where the record has already settled what asking again means.</summary>
    private static OutgoingMailFilingOutcome? SettledOutcomeOf(OutgoingEmailRecord record, OutgoingMailFiling filing) =>
        record.FindFiling(filing) switch
        {
            null => null,
            { HasUnknownOutcome: true } => OutgoingMailFilingOutcome.OutcomeUnknown,
            _ => OutgoingMailFilingOutcome.AlreadyFiled,
        };

    /// <summary>Names the code that stands for whatever ended an attempt.</summary>
    /// <remarks>
    /// A first-party failure already carries the code an operator looks up, so it is kept. What is left is genuinely
    /// unaccounted for and says so rather than borrowing a code that would mislead.
    /// </remarks>
    private static MailFathomErrorCode FailureCodeOf(Exception failure) => failure switch
    {
        MailFathomException named => named.ErrorCode,
        _ => MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly,
    };

    private static OutgoingMailFilingResult Result(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        OutgoingMailFilingOutcome outcome,
        MailFathomErrorCode? failure) => new(record.Id, filing, outcome, failure);

    /// <summary>Runs the attempt itself, from the answers that need no server to the append the server settles.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Past the issued write the append may already have reached the folder, so every way it can end has to be recorded as an outcome nobody can settle rather than raised into a retry that would file a second copy.")]
    private async Task<OutgoingMailFilingResult> AppendAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        var resolution = await this.ResolveDestinationAsync(record, filing, cancellationToken);

        if (resolution.Destination is not { } destination)
        {
            return await this.RecordFailureAsync(
                record,
                filing,
                MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable,
                OutgoingMailFilingOutcome.DestinationUnavailable);
        }

        var content = await this.contentStore.FindOutgoingContentAsync(record.Id, cancellationToken);

        if (content is null || content.RawMime.IsEmpty)
        {
            // The record and its message are written in one transaction, so a record without one describes a send that
            // can never happen rather than a message still being stored. There is nothing to append, and no later
            // attempt can invent it.
            return await this.RecordFailureAsync(
                record,
                filing,
                MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly,
                OutgoingMailFilingOutcome.Failed);
        }

        var transportSecurityPolicy = this.transportSecurityPolicies.GetPolicy(record.AccountId);

        await using var session = await this.writeSessions.OpenForWritingAsync(
            record.AccountId,
            destination.Binding,
            transportSecurityPolicy,
            cancellationToken);

        // Written only once everything that could fail without reaching a mail server already has. From here on a
        // failure leaves the row saying the append may have happened, which is what stops a second copy — so anything
        // that can be established first is established first.
        await this.commitPolicy.CommitAsync(
            (persistenceSession, token) => this.filings.RecordAppendIssuedAsync(
                persistenceSession,
                record.Id,
                filing,
                destination.Binding,
                this.timeProvider.GetUtcNow(),
                token),
            cancellationToken);

        try
        {
            var copy = await session.AppendAsync(
                content.RawMime,
                filing.Flags,
                this.timeProvider.GetUtcNow(),
                cancellationToken);

            // Committed outside the caller's cancellation, for the reason a delivery outcome is: the append has already
            // happened on somebody else's server, and a shutdown that abandoned this write would leave the row saying
            // the outcome is unknown for a copy the server named exactly.
            await this.commitPolicy.CommitAsync(
                (persistenceSession, token) => this.filings.RecordAppendConfirmedAsync(
                    persistenceSession,
                    record.Id,
                    filing,
                    copy,
                    token),
                CancellationToken.None);

            return Result(record, filing, OutgoingMailFilingOutcome.Filed, failure: null);
        }
        catch (Exception failure)
        {
            return await this.RecordFailureAsync(
                record,
                filing,
                FailureCodeOf(failure),
                OutgoingMailFilingOutcome.OutcomeUnknown);
        }
    }

    private async Task<MailboxDestinationResolution> ResolveDestinationAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        var reference = MailFolderReference.ToRole(filing.Role);

        var resolved = await this.destinations.ResolveAsync(record.AccountId, [reference], cancellationToken);

        return resolved.Find(reference);
    }

    /// <summary>Issues the withdrawal against the folder the copy is in, as that folder currently resolves.</summary>
    /// <remarks>
    /// The destination is resolved again rather than opened from the recorded path, because the path is where the folder
    /// was when the copy was appended and an alias that has since been repointed names another folder. The recorded
    /// UIDVALIDITY is what the session then refuses on, so a folder recreated in between has nothing removed from it.
    /// </remarks>
    private async Task WithdrawFromServerAsync(
        OutgoingEmailRecord record,
        OutgoingMailFilingRecord standing,
        ImapUidValidity uidValidity,
        ImapUid uid,
        CancellationToken cancellationToken)
    {
        var resolution = await this.ResolveDestinationAsync(record, standing.Filing, cancellationToken);

        if (resolution.Destination is not { } destination
            || !string.Equals(destination.Path.Value, standing.FolderPath.Value, StringComparison.Ordinal))
        {
            return;
        }

        var transportSecurityPolicy = this.transportSecurityPolicies.GetPolicy(record.AccountId);

        await using var session = await this.writeSessions.OpenForWritingAsync(
            record.AccountId,
            destination.Binding,
            transportSecurityPolicy,
            cancellationToken);

        await session.WithdrawAppendedAsync(uidValidity, uid, cancellationToken);
    }

    /// <summary>Writes the reason a copy is not where it should be onto the record, without touching its delivery.</summary>
    /// <remarks>
    /// The write runs outside the caller's cancellation on purpose, and joins no transaction of the caller's. What it
    /// records is why the owner cannot see a message in their own mail client, and a host that stopped while recording
    /// it would leave a send that says it was filed and was not.
    /// </remarks>
    private async Task<OutgoingMailFilingResult> RecordFailureAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        MailFathomErrorCode failure,
        OutgoingMailFilingOutcome outcome)
    {
        await this.filings.RecordFilingFailureAsync(record.Id, failure, CancellationToken.None);

        return Result(record, filing, outcome, failure);
    }
}
