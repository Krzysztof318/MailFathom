// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers what asking to draft a new message does, and what it refuses before anything is written.</summary>
public sealed class AuthoredMailDraftingTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Subject: a draft\r\n\r\nHello.").AsMemory();

    /// <summary>An authored message is composed, stored, and put in the drafts folder, with nothing queued to send.</summary>
    [Fact]
    public async Task SaveAsync_AuthoredMessage_StoresTheDraftAndAppendsItWithoutQueueingASend()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var harness = Harness(outgoingEmails);
        var drafting = DraftingOver(harness);

        // Act
        var draft = await drafting.SaveAsync(
            new MailDraftRequest
            {
                Account = MailAccountSelector.For(Account),
                Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "someone@example.test")],
                Subject = "a draft",
                PlainTextBody = "Hello.",
                Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftStage.Filed, draft.Stage);
        Assert.Equal(1, harness.AppendCount);
        Assert.Equal(ComposedMime.Length, harness.Contents.Peek(draft.Id).Length);
        Assert.Empty(outgoingEmails.OpenRequests);
    }

    /// <summary>An account this deployment does not serve is refused, exactly as it is when a message is sent.</summary>
    [Fact]
    public async Task SaveAsync_AccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Arrange
        var harness = Harness(new InMemoryOutgoingEmailStore());
        var drafting = DraftingOver(harness);

        // Act
        var refusal = () => drafting.SaveAsync(
            new MailDraftRequest
            {
                Account = MailAccountSelector.Create("nowhere"),
                Subject = "a draft",
                PlainTextBody = "Hello.",
                Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
            },
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(refusal);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>
    /// An account another owner owns is refused exactly as one nobody serves, so a caller cannot leave a message in
    /// somebody else's own Drafts folder for them to read as theirs.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AnAccountTheCallersOwnerDoesNotOwn_IsRefusedAndWritesNoDraft()
    {
        // Arrange
        var harness = Harness(new InMemoryOutgoingEmailStore());
        var drafting = DraftingOver(
            harness,
            authorization: AccessAuthorizations.ForOwnerGranted(
                SyntheticMailOwner.Another,
                MailFathomPermission.MailDraftsWrite));

        // Act
        var refusal = () => drafting.SaveAsync(
            new MailDraftRequest
            {
                Account = MailAccountSelector.For(Account),
                Subject = "a draft",
                PlainTextBody = "Hello.",
                Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
            },
            CancellationToken.None);

        // Assert
        var refused = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(refusal);

        // The refusal repeats what the caller named and nothing else, which is what keeps an account another owner owns
        // from being told apart from one this deployment never served.
        Assert.Equal(MailAccountSelector.For(Account), refused.RequestedAccount);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A list longer than any record could hold is refused before the contact book is read at all.</summary>
    [Fact]
    public async Task SaveAsync_MoreRecipientsThanARecordCanHold_IsRefusedBeforeTheBookIsRead()
    {
        // Arrange
        var harness = Harness(new InMemoryOutgoingEmailStore());
        var book = new InMemoryContactBookStore();
        var drafting = DraftingOver(harness, book);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafting.SaveAsync(
                new MailDraftRequest
                {
                    Account = MailAccountSelector.For(Account),
                    Recipients =
                    [
                        .. Enumerable
                            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
                            .Select(ordinal => NamedRecipient.AtAddress(
                                OutgoingRecipientRole.To,
                                $"person{ordinal}@example.test")),
                    ],
                    Subject = "a draft",
                    PlainTextBody = "Hello.",
                    Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
                },
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Equal(0, book.BatchedLookupCount);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A refused composition is reported as itself, and nothing about the draft is written down.</summary>
    [Fact]
    public async Task SaveAsync_CompositionRefusesTheMessage_ReportsItAndWritesNothing()
    {
        // Arrange
        var harness = Harness(new InMemoryOutgoingEmailStore());
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .ComposeDraft(
                Arg.Any<MailAccountId>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(MailDraftComposition.Refused(new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.SenderUnconfigured,
                AuthoredEmailField.Sender,
                Bound: null)));

        var drafting = DraftingOver(harness, composer: composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafting.SaveAsync(
                new MailDraftRequest
                {
                    Account = MailAccountSelector.For(Account),
                    Subject = "a draft",
                    PlainTextBody = "Hello.",
                    Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
                },
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailSendingUnavailable, refusal.ErrorCode);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Equal(0, harness.AppendCount);
    }

    private static MailDraftHarness Harness(InMemoryOutgoingEmailStore outgoingEmails)
    {
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            outgoingEmails,
            MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8)));

        harness.MapDraftsFolder(Account);

        return harness;
    }

    private static AuthoredMailDrafting DraftingOver(
        MailDraftHarness harness,
        InMemoryContactBookStore? book = null,
        IAuthoredEmailComposer? composer = null,
        AccessAuthorization? authorization = null)
    {
        // One authorization, for the reason AuthoredSendGovernors.Governing states: the mailboxes the draft is placed
        // in, the book the recipients are resolved out of, and the caller the drafting runs for are one scoped instance
        // in production.
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDraftsWrite);

        return new AuthoredMailDrafting(
            OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account)),
            new NamedRecipientResolver(
                book ?? new InMemoryContactBookStore(),
                ContactBookOwnerships.For(callerAuthorization)),
            composer ?? ComposingAuthoredEmails.ThatComposesDrafts(ComposedMime),
            harness.Book,
            callerAuthorization);
    }
}
