// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the cleaned-body route puts on the wire.</summary>
/// <remarks>
/// The pass itself is covered where it happens. What is asserted here is the transport, and above all the one property a
/// client draws its pane from: every answer carries a document and names what came of the cleaning, so a reader is shown
/// the message either way and told when the third rendering did not happen.
/// </remarks>
public sealed class ClientMailCleanedBodyEndpointTests
{
    private static readonly Guid Message = new("11111111-1111-1111-1111-111111111111");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void CleanedMailBodyRoute_IsThePathAClientComposes() =>
        Assert.Equal("/messages/{storedEmailId:guid}/body/cleaned", ClientMailCleanedBodyEndpoint.CleanedMailBodyRoute);

    /// <summary>It stands beside the body route rather than replacing it, so a reader choosing the third rendering reads both.</summary>
    [Fact]
    public void CleanedMailBodyRoute_IsItsOwnRouteRatherThanTheBodyRoute() =>
        Assert.NotEqual(ClientMailBodyEndpoint.MailBodyRoute, ClientMailCleanedBodyEndpoint.CleanedMailBodyRoute);

    [Fact]
    public void For_ACleanedBody_PublishesTheCleanedDocumentAndSaysTheCleaningHappened()
    {
        // Arrange
        var cleaned = new CleanedMailBody(MailBodyCleaningOutcome.Cleaned, DocumentSaying("Your code is 558132"));

        // Act
        var response = ClientMailCleanedBodyResponse.For(Message, cleaned);

        // Assert
        Assert.Equal(Message, response.StoredEmailId);
        Assert.Equal(nameof(MailBodyCleaningOutcome.Cleaned), response.Cleaning);
        Assert.Equal(cleaned.Document, response.Document);
    }

    /// <summary>
    /// Every reason a cleaning did not happen travels as its own name beside the ordinary reduced document, which is what
    /// lets a client say what did not happen instead of drawing a view that silently became a different view.
    /// </summary>
    [Theory]
    [InlineData(MailBodyCleaningOutcome.NotActivated)]
    [InlineData(MailBodyCleaningOutcome.AllowanceExhausted)]
    [InlineData(MailBodyCleaningOutcome.ProviderUnavailable)]
    [InlineData(MailBodyCleaningOutcome.AnswerRejected)]
    [InlineData(MailBodyCleaningOutcome.NothingToClean)]
    public void For_ACleaningThatDidNotHappen_PublishesTheReducedDocumentBesideTheReason(
        MailBodyCleaningOutcome outcome)
    {
        // Arrange
        var reduced = DocumentSaying("Your code is 558132");

        // Act
        var response = ClientMailCleanedBodyResponse.For(Message, new CleanedMailBody(outcome, reduced));

        // Assert
        Assert.Equal(outcome.ToString(), response.Cleaning);
        Assert.Equal(reduced, response.Document);
    }

    /// <summary>A body that carried no document at all says so by absence, which is what the body route says about that message too.</summary>
    [Fact]
    public void For_ABodyThatCarriedNoDocument_PublishesNone()
    {
        // Act
        var response = ClientMailCleanedBodyResponse.For(
            Message,
            new CleanedMailBody(MailBodyCleaningOutcome.NothingToClean, Document: null));

        // Assert
        Assert.Null(response.Document);
    }

    /// <summary>A message nobody named is nothing to read rather than a refusal, and it costs no provider call to answer.</summary>
    [Fact]
    public async Task ReadCleanedBodyAsync_AnEmptyIdentity_AnswersThatThereIsNoSuchMessage()
    {
        // Act
        var result = await ClientMailCleanedBodyEndpoint.ReadCleanedBodyAsync(
            Guid.Empty,
            remoteImages: null,
            CleaningOverNoMail(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A message this caller does not hold does not exist as far as this surface is concerned, exactly as on the body route.</summary>
    [Fact]
    public async Task ReadCleanedBodyAsync_AMessageThisCallerDoesNotHold_AnswersThatThereIsNoSuchMessage()
    {
        // Act
        var result = await ClientMailCleanedBodyEndpoint.ReadCleanedBodyAsync(
            Message,
            remoteImages: true,
            CleaningOverNoMail(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task ReadCleanedBodyAsync_WithoutTheUseCase_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClientMailCleanedBodyEndpoint.ReadCleanedBodyAsync(
            Message,
            remoteImages: null,
            cleaning: null!,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The pass over a read that finds nothing, which is what both absent-message cases go through. The cleaner behind it
    /// refuses to be asked, so these establish that neither case spends a provider call to find out the message is absent.
    /// </summary>
    private static MailBodyCleaning CleaningOverNoMail()
    {
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        summaries.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmailSummary?>(null));

        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();
        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        var content = new EmailContentReader(
            summaries,
            Substitute.For<IEmailThreadReader>(),
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                Substitute.For<ICallerMailAccountCatalog>(),
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            Substitute.For<IAttachmentDownloadLinkIssuer>(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        return new MailBodyCleaning(content, new UnaskableMailBodyCleaner());
    }

    private static MailDocument DocumentSaying(string text) => MailDocument.Reduced(
        [
            new MailParagraphBlock(
                [new MailInlineRun(text, MailTextEmphasis.None, Foreground: null, Link: null)],
                MailBlockAlignment.Inherited),
        ],
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false);

    /// <summary>A cleaner that fails the test if it is asked anything, which is what makes "no provider call" an assertion.</summary>
    private sealed class UnaskableMailBodyCleaner : IMailBodyCleaner
    {
        public bool IsActive => true;

        public Task<MailBodyCleaningProposal> ProposeAsync(
            CleanableMailBody body,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A message the caller does not hold must not reach a provider.");
    }
}
