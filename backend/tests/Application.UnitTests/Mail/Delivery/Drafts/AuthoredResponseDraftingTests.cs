// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers what asking to draft an answer to stored mail refuses, and what it leaves behind when it does.</summary>
/// <remarks>
/// What the answer itself is made of — the quotation, the threading identifiers, the carried files — belongs to the
/// authoring beneath this and is covered where that lives. What is asserted here is the half this use case owns: the
/// grant, the bound it checks before the stored mail is read, and that a refusal writes no draft.
/// </remarks>
public sealed class AuthoredResponseDraftingTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An answer to a message this deployment does not hold is refused, and no folder is opened for it.</summary>
    [Fact]
    public async Task SaveAsync_AnsweredEmailThisDeploymentDoesNotHold_IsRefusedAndWritesNoDraft()
    {
        // Arrange
        var harness = Harness();
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        var drafting = DraftingOver(harness, summaries);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafting.SaveAsync(Request(), CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.AnsweredEmailUnavailable, refusal.ErrorCode);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>
    /// A recipient list longer than any record could hold is refused before the answered mail is read, because the
    /// read carries what the caller supplied.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MoreRecipientsThanARecordCanHold_IsRefusedBeforeTheStoredMailIsRead()
    {
        // Arrange
        var harness = Harness();
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        var drafting = DraftingOver(harness, summaries);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafting.SaveAsync(
                Request() with
                {
                    Recipients =
                    [
                        .. Enumerable
                            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
                            .Select(ordinal => NamedRecipient.AtAddress(
                                OutgoingRecipientRole.To,
                                $"person{ordinal}@example.test")),
                    ],
                },
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Empty(summaries.ReceivedCalls());
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>Drafting is its own grant, and reading the mail an answer would quote is not it.</summary>
    [Fact]
    public async Task SaveAsync_CallerWithoutTheDraftingGrant_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var harness = Harness();
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        var drafting = DraftingOver(
            harness,
            summaries,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = () => drafting.SaveAsync(Request(), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
        Assert.Empty(summaries.ReceivedCalls());
    }

    private static MailResponseDraftRequest Request() => new()
    {
        AnsweredEmailId = StoredEmailId.Create(Guid.CreateVersion7(Moment)),
        Act = AuthoredResponseAct.Reply,
        PlainTextBody = "Thank you.",
        Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
    };

    private static MailDraftHarness Harness()
    {
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            new InMemoryOutgoingEmailStore(),
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

    private static AuthoredResponseDrafting DraftingOver(
        MailDraftHarness harness,
        IStoredEmailSummaryReader summaries,
        AccessAuthorization? authorization = null)
    {
        var granted = authorization ?? AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailDraftsWrite,
            MailFathomPermission.MailRead);

        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var authoring = new StoredEmailResponseAuthoring(
            summaries,
            ContentStores.Substituted(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IEmailAttachmentContentReader>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            Substitute.For<IOutgoingSenderIdentityReader>(),
            new NamedRecipientResolver(new InMemoryContactBookStore(), ContactBookOwnerships.For(granted)),
            Bounds(),
            granted);

        return new AuthoredResponseDrafting(
            authoring,
            Substitute.For<IAuthoredEmailComposer>(),
            harness.Book,
            granted);
    }

    private static OutgoingEmailBounds Bounds() => new()
    {
        MaxRecipientCount = 8,
        MaxBodyCharacters = 4096,
        MaxAttachmentCount = 3,
        MaxAttachmentBytes = 128,
        MaxMessageBytes = 300,
    };
}
