// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Puts one copy of a message MailFathom composed into a folder of the account it belongs to.</summary>
/// <remarks>
/// <para>
/// Every copy this system files goes through here, whatever kind of message it is a copy of. A sent copy, a mirror of
/// a message still waiting to go out, and a revision of a draft differ in the role their folder plays, the flags the
/// copy carries, and the durable record each is written onto — and in nothing else, so the append itself is one call
/// and a fourth kind of copy inherits it rather than restating it.
/// </para>
/// <para>
/// <b>The order of the two writes is the whole of the safety here.</b> An <c>APPEND</c> issued twice is a second
/// message in somebody's folder rather than a repeat of the first, and nothing that folder shows afterwards tells the
/// two apart. So the caller's record of the copy is made durable before the command goes out, and everything that can
/// fail without leaving a copy in the folder happens before that write. Opening the session does reach the server —
/// it connects, authenticates, and selects the folder — but none of that puts a message anywhere, which is what makes
/// an attempt that ended before the issued write repeatable. The two writes are the caller's because the row they
/// move belongs to the caller, but when each of them runs is not: they are taken as callbacks so the ordering cannot
/// hold on one filing path and lapse on another.
/// </para>
/// <para>
/// Nothing here raises for a copy that could not be filed. Past the issued write the append may already be in the
/// folder, so every way it can end is classified into a code and returned, rather than raised into a retry that would
/// leave the owner with two copies of their own message.
/// </para>
/// </remarks>
public sealed class MailboxCopyAppender
{
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly MailboxDestinationResolver destinations;
    private readonly IEmailContentStore contentStore;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the appender from the session it appends through and the store it reads the message from.</summary>
    /// <param name="writeSessions">Opens the one session able to change a mailbox.</param>
    /// <param name="destinations">Turns the role a filing names into the folder of the account it means.</param>
    /// <param name="contentStore">Holds the stored MIME the copy is appended from.</param>
    /// <param name="transportSecurityPolicies">Supplies the connection and authentication policy the append obeys.</param>
    /// <param name="timeProvider">Stamps the copy's internal date.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailboxCopyAppender(
        IMailboxWriteSessionFactory writeSessions,
        MailboxDestinationResolver destinations,
        IEmailContentStore contentStore,
        IMailTransportSecurityPolicyReader transportSecurityPolicies,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicies);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.writeSessions = writeSessions;
        this.destinations = destinations;
        this.contentStore = contentStore;
        this.transportSecurityPolicies = transportSecurityPolicies;
        this.timeProvider = timeProvider;
    }

    /// <summary>Names the code that stands for whatever ended an attempt.</summary>
    /// <param name="failure">What ended it.</param>
    /// <returns>The code an operator looks the failure up by.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A first-party failure already carries the code an operator looks up, so it is kept. What is left is genuinely
    /// unaccounted for and says so rather than borrowing a code that would mislead.
    /// </remarks>
    public static MailFathomErrorCode FailureCodeOf(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure switch
        {
            MailFathomException named => named.ErrorCode,
            _ => MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly,
        };
    }

    /// <summary>Appends one copy, having made the caller's record of it durable first.</summary>
    /// <param name="account">The account whose folder the copy goes into.</param>
    /// <param name="filing">Which place the copy goes into, which decides the role and the flags.</param>
    /// <param name="source">The stored message the copy is appended from.</param>
    /// <param name="recordIssuedAsync">Commits the caller's record that an append is about to be issued into the resolved folder.</param>
    /// <param name="recordConfirmedAsync">Commits the caller's record of what the server said, which runs outside the caller's cancellation.</param>
    /// <param name="cancellationToken">Cancels everything up to and including the append.</param>
    /// <returns>What the server said about the copy, or the reason nothing was appended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source" />, <paramref name="recordIssuedAsync" />, or <paramref name="recordConfirmedAsync" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filing" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// A caller that stops before the issued write gets an <see cref="OperationCanceledException" /> and no
    /// <c>APPEND</c> issued, so the next pass files the copy as though this attempt had never started. That says
    /// nothing about whether the server was reached: a stop while the session is opening can arrive after the
    /// connection, the authentication, or the folder selection, none of which leaves anything in the folder. Past the
    /// issued write nothing is raised, because a shutdown there says nothing about a command that may be in flight.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Past the issued write the append may already have reached the folder, so every way it can end has to be recorded as an outcome nobody can settle rather than raised into a retry that would file a second copy.")]
    public async Task<MailboxCopyAppendResult> AppendAsync(
        MailAccountIdentity account,
        OutgoingMailFiling filing,
        MailboxCopySource source,
        Func<MailFolderResolution, CancellationToken, Task> recordIssuedAsync,
        Func<AppendedMailCopy, Task> recordConfirmedAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recordIssuedAsync);
        ArgumentNullException.ThrowIfNull(recordConfirmedAsync);

        if (!filing.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct names no filing.", nameof(filing));
        }

        if (await this.ResolveDestinationAsync(account, filing.Role, cancellationToken) is not { } destination)
        {
            return MailboxCopyAppendResult.DestinationUnavailable();
        }

        var content = await source.FindContentAsync(this.contentStore, cancellationToken);

        if (content is null || content.RawMime.IsEmpty)
        {
            // A record and its message are written in one transaction, so a record without one describes a copy that
            // can never be appended rather than a message still being stored. No later attempt can invent it.
            return MailboxCopyAppendResult.MessageUnavailable();
        }

        var transportSecurityPolicy = this.transportSecurityPolicies.GetPolicy(account.Id);

        await using var session = await this.writeSessions.OpenForWritingAsync(
            account.Id,
            destination.Binding,
            transportSecurityPolicy,
            cancellationToken);

        // Written only once everything that can fail without leaving a copy in the folder already has. From here on a
        // failure leaves the caller's row saying the append may have happened, which is what stops a second copy — so
        // anything that can be established first is established first.
        await recordIssuedAsync(destination.Binding, cancellationToken);

        try
        {
            var copy = await session.AppendAsync(
                content.RawMime,
                filing.Flags,
                this.timeProvider.GetUtcNow(),
                cancellationToken);

            // Handed over without the caller's cancellation, for the reason a delivery outcome is: the append has
            // already happened on somebody else's server, and a shutdown that abandoned this write would leave the row
            // saying the outcome is unknown for a copy the server named exactly.
            await recordConfirmedAsync(copy);

            return MailboxCopyAppendResult.Appended(copy);
        }
        catch (Exception failure)
        {
            return MailboxCopyAppendResult.OutcomeUnknown(FailureCodeOf(failure));
        }
    }

    /// <summary>Finds the folder of the account that plays a role, as it currently resolves.</summary>
    private async Task<MailboxDestination?> ResolveDestinationAsync(
        MailAccountIdentity account,
        MailFolderSpecialUse role,
        CancellationToken cancellationToken)
    {
        var reference = MailFolderReference.ToRole(role);

        var resolved = await this.destinations.ResolveAsync(account, [reference], cancellationToken);

        return resolved.Find(reference).Destination;
    }
}
