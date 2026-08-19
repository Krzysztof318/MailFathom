// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Governance;

public sealed class AuthoredSendGovernorTests
{
    private const string CallerIdentity = "test-caller";

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Recorded =
        DateTimeOffset.Parse("2026-08-19T09:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>A recipient the deployment may never write to is refused here as well as beneath, before anything is written.</summary>
    [Fact]
    public async Task RequirePermittedAsync_RecipientTheDeploymentMayNotWriteTo_IsRefusedOnThisSurface()
    {
        // Arrange
        var governor = AuthoredSendGovernors.Governing(
            recipientPolicy: OutgoingRecipientPolicy.Create(
                allowed: [],
                denied: [DomainRule("elsewhere.test")]));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(
                [NamedByCaller("stranger@elsewhere.test")],
                RequestTo("stranger@elsewhere.test"),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientRefusedByPolicy, refusal.ErrorCode);
    }

    /// <summary>A caller past a ceiling of its own is refused, and the refusal names a ceiling rather than a policy.</summary>
    [Fact]
    public async Task RequirePermittedAsync_CallerPastItsOwnCeiling_IsRefused()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            new FakeTimeProvider(Recorded));
        var governor = AuthoredSendGovernors.Governing(ledger: ledger);
        await governor.RequirePermittedAsync(
            [NamedByCaller("anna@example.test")],
            AskedAs("send-0", "anna@example.test"),
            CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(
                [NamedByCaller("anna@example.test")],
                RequestTo("anna@example.test"),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailCeilingReached, refusal.ErrorCode);
    }

    /// <summary>A deployment that refuses what it cannot vouch for refuses the whole message, naming nobody.</summary>
    [Fact]
    public async Task RequirePermittedAsync_UnvouchedRecipientUnderTheRefusingPosture_RefusesTheWholeMessage()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna", "anna@example.test"));
        var governor = AuthoredSendGovernors.Governing(
            settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse),
            contacts: book);

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => governor.RequirePermittedAsync(
                [NamedByCaller("anna@example.test"), NamedByCaller("accomplice@elsewhere.test")],
                RequestTo("anna@example.test", "accomplice@elsewhere.test"),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientUnvouched, refusal.ErrorCode);
        Assert.DoesNotContain("accomplice", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("elsewhere.test", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An answer is addressed by the message it answers, so the refusing posture still lets a reply be sent.</summary>
    [Fact]
    public async Task RequirePermittedAsync_RecipientDerivedFromTheAnsweredEmail_IsAdmittedUnderTheRefusingPosture()
    {
        // Arrange
        var governor = AuthoredSendGovernors.Governing(
            settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse),
            contacts: new InMemoryContactBookStore());

        // Act
        var permit = await governor.RequirePermittedAsync(
            [Derived("stranger@elsewhere.test")],
            RequestTo("stranger@elsewhere.test"),
            CancellationToken.None);

        // Assert
        Assert.Equal(0, permit.UnvouchedRecipientCount);
    }

    /// <summary>The admitting posture sends the message and still says, of that send, that nobody here vouched for it.</summary>
    [Fact]
    public async Task RecordAsync_UnvouchedRecipientUnderTheAdmittingPosture_RecordsThatNobodyVouchedForOne()
    {
        // Arrange
        var auditor = new RecordingAuthoredSendAuditor();
        var governor = AuthoredSendGovernors.Governing(
            contacts: new InMemoryContactBookStore(),
            auditor: auditor,
            timeProvider: new FakeTimeProvider(Recorded));

        // Act
        var permit = await governor.RequirePermittedAsync(
            [NamedByCaller("accomplice@elsewhere.test")],
            RequestTo("accomplice@elsewhere.test"),
            CancellationToken.None);
        await governor.RecordAsync(
            permit,
            AuthoredSendAct.Forward,
            RecordOf("accomplice@elsewhere.test"),
            CancellationToken.None);

        // Assert
        var recorded = Assert.Single(auditor.Recorded);
        Assert.Equal(1, recorded.UnvouchedRecipientCount);
    }

    /// <summary>The record names who asked, under what, for which act, and which record came of it — and nothing else.</summary>
    [Fact]
    public async Task RecordAsync_SendThatWasWrittenDown_RecordsWhoAskedAndCarriesNothingOfTheMessage()
    {
        // Arrange
        var auditor = new RecordingAuthoredSendAuditor();
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna", "anna@example.test"));
        var governor = AuthoredSendGovernors.Governing(
            contacts: book,
            auditor: auditor,
            timeProvider: new FakeTimeProvider(Recorded));
        var record = RecordOf("anna@example.test");

        // Act
        var permit = await governor.RequirePermittedAsync(
            [NamedByCaller("anna@example.test")],
            RequestTo("anna@example.test"),
            CancellationToken.None);
        await governor.RecordAsync(permit, AuthoredSendAct.NewMessage, record, CancellationToken.None);

        // Assert
        var recorded = Assert.Single(auditor.Recorded);
        Assert.Equal(CallerIdentity, recorded.Caller);
        Assert.Equal(MailFathomPermission.MailSend, recorded.Grant);
        Assert.Equal(AuthoredSendAct.NewMessage, recorded.Act);
        Assert.Equal(Account, recorded.AccountId);
        Assert.Equal(record.Id, recorded.OutgoingEmailId);
        Assert.Equal(1, recorded.RecipientCount);
        Assert.Equal(0, recorded.UnvouchedRecipientCount);
        Assert.Equal(Recorded, recorded.OccurredAt);
        Assert.DoesNotContain("anna@example.test", recorded.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A retried call asks for the send it first asked for, so one message of the caller's allowance is spent.</summary>
    [Fact]
    public async Task RequirePermittedAsync_SameSendTwice_SpendsOneMessageOfTheCallersAllowance()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            new FakeTimeProvider(Recorded));
        var governor = AuthoredSendGovernors.Governing(ledger: ledger);

        // Act
        var first = await governor.RequirePermittedAsync(
            [NamedByCaller("anna@example.test")],
            RequestTo("anna@example.test"),
            CancellationToken.None);
        var retried = await governor.RequirePermittedAsync(
            [NamedByCaller("anna@example.test")],
            RequestTo("anna@example.test"),
            CancellationToken.None);

        // Assert
        Assert.Equal(first.Caller, retried.Caller);
    }

    /// <summary>A send governed under no principal at all is a use case that never established who asked.</summary>
    [Fact]
    public async Task RequirePermittedAsync_NoPrincipal_IsADefect()
    {
        // Arrange
        var governor = AuthoredSendGovernors.Governing(
            authorization: AccessAuthorizations.ForPrincipal(principal: null));

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => governor.RequirePermittedAsync(
                [NamedByCaller("anna@example.test")],
                RequestTo("anna@example.test"),
                CancellationToken.None));
    }

    private static OutgoingRecipientRule DomainRule(string domain)
    {
        if (!OutgoingRecipientRule.TryCreateForDomain(domain, out var rule))
        {
            throw new InvalidOperationException($"The test domain '{domain}' names no organization.");
        }

        return rule;
    }

    private static AuthoredEmailRecipient NamedByCaller(string address) => new(OutgoingRecipientRole.To, address);

    private static AuthoredEmailRecipient Derived(string address) => new(
        OutgoingRecipientRole.To,
        address,
        DisplayName: null,
        Contact: null,
        AuthoredRecipientProvenance.DerivedFromAnsweredEmail);

    private static OutgoingEmailRequest RequestTo(params string[] addresses) => AskedAs("send-1", addresses);

    private static OutgoingEmailRequest AskedAs(string requesterIdentity, params string[] addresses) =>
        OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command(requesterIdentity),
            [.. addresses.Select(address => OutgoingRecipient.Create(Address(address), OutgoingRecipientRole.To))]);

    private static OutgoingEmailRecord RecordOf(params string[] addresses) => new()
    {
        Id = RecordNumber(7),
        AccountId = Account,
        Requester = OutgoingEmailRequester.Command("send-1"),
        Recipients =
        [
            .. addresses.Select(address => OutgoingRecipientOutcome.Unanswered(
                OutgoingRecipient.Create(Address(address), OutgoingRecipientRole.To))),
        ],
        Stage = OutgoingEmailStage.Recorded,
        MimeByteLength = 512,
        AttemptCount = 0,
        RecordedAt = Recorded,
        StageChangedAt = Recorded,
        AvailableAt = Recorded,
        LastFailure = null,
        LastReplyCode = null,
        Filings = [],
        LastFilingFailure = null,
    };

    private static OutgoingEmailId RecordNumber(int number) =>
        OutgoingEmailId.Create(new Guid(number, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    private static Contact ContactOf(string displayName, params string[] addresses) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(Recorded)),
        ContactDisplayName.Create(displayName),
        [.. addresses.Select(Address)],
        Address(addresses[0]),
        note: null,
        ContactOrigin.Asserted,
        Recorded,
        Recorded);

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }

    /// <summary>Keeps what the governance recorded, which is the only way to assert what a send is answerable for.</summary>
    private sealed class RecordingAuthoredSendAuditor : IAuthoredSendAuditor
    {
        private readonly List<AuthoredSend> recorded = [];

        public IReadOnlyList<AuthoredSend> Recorded => this.recorded;

        public Task RecordAuthoredSendAsync(AuthoredSend send, CancellationToken cancellationToken)
        {
            this.recorded.Add(send);

            return Task.CompletedTask;
        }
    }
}
