// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations;

public sealed class MailboxMutationPerformerTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    private static readonly MailFolderResolution InboxFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX", '/'));

    private static readonly RemoteFolderPath ArchivePath = RemoteFolderPath.Create("Archive", '/');

    private static readonly MailTransportSecurityPolicy TransportPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private static readonly RemoteEmailPlacement ArchivedAt = RemoteEmailPlacement.Reported(
        ImapUidValidity.Create(11U),
        ImapUid.Create(7U));

    /// <summary>Nothing may reach a mail server before the intent is durable, because the record is what a crash leaves behind.</summary>
    [Fact]
    public async Task PerformAsync_Always_WritesTheRecordDownBeforeOpeningTheWriteSession()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        var recordExistedWhenTheSessionOpened = false;
        context.WriteSessionFactory
            .OpenForWritingAsync(Account.Id, InboxFolder, TransportPolicy, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                recordExistedWhenTheSessionOpened = context.Store.OpenedRecordCount == 1;

                return Task.FromResult(context.WriteSession);
            });

        // Act
        await context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None);

        // Assert
        Assert.True(recordExistedWhenTheSessionOpened);
    }

    /// <summary>The idempotency identity's whole purpose: the same request twice performs one mutation.</summary>
    [Fact]
    public async Task PerformAsync_AskedTwiceForTheSameRequest_PerformsOneMutationAndAnswersTheSecondFromTheRecord()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        context.AnswerRelocationWith(ArchivedAt);

        // Act
        var first = await context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None);
        var second = await context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailboxMutationStatus.Performed, first.Status);
        Assert.Equal(MailboxMutationStatus.AlreadyPerformed, second.Status);
        Assert.Equal(first.RecordId, second.RecordId);
        Assert.Equal(1, context.Store.OpenedRecordCount);
        await context.WriteSessionFactory.Received(1).OpenForWritingAsync(
            Account.Id,
            InboxFolder,
            TransportPolicy,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The <c>COPYUID</c> a server supplied is kept, so a later reader is answered from the record rather than from a guess.</summary>
    [Fact]
    public async Task PerformAsync_WhenTheServerNamedThePlacement_KeepsItOnTheRecord()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        context.AnswerRelocationWith(ArchivedAt);

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(ArchivedAt, outcome.Placement);
        Assert.Equal(ArchivedAt, record.Placement);
        Assert.Equal(MailboxMutationStage.Completed, record.Stage);
    }

    /// <summary>
    /// The crash after a copy whose response was never read. Nothing in the destination folder distinguishes a copy
    /// MailFathom made from one a person made, so the command is never issued again and the record stays as it is.
    /// </summary>
    [Fact]
    public async Task PerformAsync_RecordLeftAtPlacementIssued_IssuesNothingAndReportsTheOutcomeUnknown()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        await context.OpenRecordFor(request);
        context.Store.Arrange(request, record => record with
        {
            Stage = MailboxMutationStage.PlacementIssued,
            RequiresSourceRemoval = true,
        });

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        Assert.Equal(MailboxMutationStatus.OutcomeUnknown, outcome.Status);

        // The stage a person has to resolve is the one that must say why, or it reads as merely old.
        var record = context.Store.RecordOf(request);
        Assert.Equal(MailboxMutationStage.PlacementIssued, record.Stage);
        Assert.Equal(MailFathomErrorCode.MailboxMutationOutcomeUnknown, record.LastFailure);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mutation nothing will attempt again is answered from the record without a connection.</summary>
    [Fact]
    public async Task PerformAsync_RecordAlreadyAbandoned_IssuesNothing()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        await context.OpenRecordFor(request);
        context.Store.Arrange(request, record => record with { Stage = MailboxMutationStage.Abandoned });

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        Assert.Equal(MailboxMutationStatus.Abandoned, outcome.Status);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Counting first is what makes an attempt that kills the process count against the bound.</summary>
    [Fact]
    public async Task PerformAsync_WhenTheAttemptFails_HasAlreadyCountedItAndKeepsTheStageItReached()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        context.FailRelocationWith(new MailboxUnavailableException(Account.Id, InboxFolder.Alias, new TimeoutException()));

        // Act
        await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(MailboxMutationStage.Recorded, record.Stage);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, record.LastFailure);
    }

    /// <summary>The bound exists so a change that cannot be made becomes visible instead of being retried forever.</summary>
    [Fact]
    public async Task PerformAsync_WhenTheLastPermittedAttemptFails_AbandonsTheRecordWithThatFailure()
    {
        // Arrange
        var context = new PerformerContext(maximumAttempts: 2);
        var request = RelocationRequest();
        context.FailRelocationWith(new MailboxUnavailableException(Account.Id, InboxFolder.Alias, new TimeoutException()));

        // Act
        await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));
        await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(MailboxMutationStage.Abandoned, record.Stage);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, record.LastFailure);
    }

    /// <summary>
    /// An attempt that kills the process records no failure of its own, so the bound has to be read from the count
    /// rather than from a failure being present. Otherwise a mutation that crashes the host would be retried forever.
    /// </summary>
    [Fact]
    public async Task PerformAsync_WhenEveryAttemptCrashedWithoutRecordingAFailure_AbandonsOnTheNextCall()
    {
        // Arrange
        var context = new PerformerContext(maximumAttempts: 3);
        var request = RelocationRequest();
        await context.OpenRecordFor(request);
        context.Store.Arrange(request, record => record with { AttemptCount = 3 });

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(MailboxMutationStatus.Abandoned, outcome.Status);
        Assert.Equal(MailboxMutationStage.Abandoned, record.Stage);
        Assert.Equal(MailFathomErrorCode.MailboxMutationAttemptsExhausted, record.LastFailure);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A server that advertises no way to carry the change safely will advertise none tomorrow, so spending the attempt
    /// bound on it would only delay the same answer while looking busy.
    /// </summary>
    [Fact]
    public async Task PerformAsync_WhenTheServerCannotCarryTheMutation_AbandonsOnTheFirstAttempt()
    {
        // Arrange
        var context = new PerformerContext(maximumAttempts: 5);
        var request = RelocationRequest();
        context.FailRelocationWith(new MailboxMutationUnsupportedException(
            Account.Id,
            InboxFolder.Alias,
            MailboxMutation.Relocate.Name,
            "UIDPLUS extension (RFC 4315)"));

        // Act
        await Assert.ThrowsAsync<MailboxMutationUnsupportedException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(MailboxMutationStage.Abandoned, record.Stage);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxMutationUnsupported, record.LastFailure);
    }

    /// <summary>
    /// A destination folder the server does not have is an answer rather than a bad moment, so the change is given up
    /// on at once instead of spending a login per attempt to be told the same thing five times.
    /// </summary>
    [Fact]
    public async Task PerformAsync_WhenTheDestinationFolderIsMissing_AbandonsOnTheFirstAttempt()
    {
        // Arrange
        var context = new PerformerContext(maximumAttempts: 5);
        var request = RelocationRequest();
        context.FailRelocationWith(new MailboxDestinationFolderMissingException(
            Account.Id,
            InboxFolder.Alias,
            MailboxMutation.Relocate,
            new InvalidOperationException("The folder could not be found.")));

        // Act
        await Assert.ThrowsAsync<MailboxDestinationFolderMissingException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(MailboxMutationStage.Abandoned, record.Stage);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxMutationDestinationMissing, record.LastFailure);
    }

    /// <summary>A failure MailFathom did not raise itself still has to leave a code on the record an operator can read.</summary>
    [Fact]
    public async Task PerformAsync_WhenTheAttemptFailsUnexpectedly_RecordsTheUnclassifiedCode()
    {
        // Arrange
        var context = new PerformerContext();
        var request = RelocationRequest();
        context.FailRelocationWith(new InvalidOperationException("Something nobody classified."));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));

        // Assert
        Assert.Equal(
            MailFathomErrorCode.MailboxMutationFailedUnexpectedly,
            context.Store.RecordOf(request).LastFailure);
    }

    /// <summary>Each mutation reaches the operation it names, and nothing else does.</summary>
    [Fact]
    public async Task PerformAsync_ForEachMutation_IssuesOnlyTheOperationItNames()
    {
        // Arrange
        var context = new PerformerContext();
        var occurrence = Occurrence(42U);
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var requester = MailboxMutationRequester.Rule("file-newsletters", "3");

        // Act
        await context.Performer.PerformAsync(
            MailboxMutationRequest.Delete(
                storedEmailId, SyntheticMailOwner.Deployment,
                occurrence,
                requester,
                AuthoredDeleteEmailDisposition.RetainLocalCopy),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);
        await context.Performer.PerformAsync(
            MailboxMutationRequest.SetSeen(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, isSeen: true),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);
        await context.Performer.PerformAsync(
            MailboxMutationRequest.Copy(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, ArchivePath),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        await context.WriteSession.Received(1).DeleteAsync(
            occurrence,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
        await context.WriteSession.Received(1).SetSeenAsync(
            occurrence,
            true,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
        await context.WriteSession.Received(1).CopyAsync(
            occurrence,
            ArchivePath,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
        await context.WriteSession.DidNotReceive().RelocateAsync(
            Arg.Any<EmailOccurrenceId>(),
            Arg.Any<RemoteFolderPath>(),
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The three keyword mutations differ only in what they do with the same list, so each must reach its own operation.</summary>
    /// <remarks>
    /// A replacement carried as an addition would leave a label the rule asked to be rid of, and an addition carried as
    /// a replacement would strip every label the message already had. The performer decides which one is issued from the
    /// mutation on the record rather than from the keywords, which look identical in all three.
    /// </remarks>
    [Fact]
    public async Task PerformAsync_ForEachKeywordMutation_IssuesOnlyTheOperationItNames()
    {
        // Arrange
        var context = new PerformerContext();
        var occurrence = Occurrence(42U);
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var requester = MailboxMutationRequester.Rule("label-invoices", "4");
        var labels = AuthoredMailKeywords.Create(["$Todo"]);

        // Act
        await context.Performer.PerformAsync(
            MailboxMutationRequest.AddKeywords(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, labels),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);
        await context.Performer.PerformAsync(
            MailboxMutationRequest.RemoveKeywords(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, labels),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);
        await context.Performer.PerformAsync(
            MailboxMutationRequest.SetKeywords(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, AuthoredMailKeywords.None),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        await context.WriteSession.Received(1).AddKeywordsAsync(
            occurrence,
            labels,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
        await context.WriteSession.Received(1).RemoveKeywordsAsync(
            occurrence,
            labels,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
        await context.WriteSession.Received(1).SetKeywordsAsync(
            occurrence,
            AuthoredMailKeywords.None,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Both directions of the star are one authored act, and the one the operator wrote is the one issued.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PerformAsync_ForAFlaggedStateChange_CarriesTheAuthoredDirectionIntoTheSession(bool isFlagged)
    {
        // Arrange
        var context = new PerformerContext();
        var occurrence = Occurrence(42U);
        var request = MailboxMutationRequest.SetFlagged(
            StoredEmailId.Create(Guid.CreateVersion7()), SyntheticMailOwner.Deployment,
            occurrence,
            MailboxMutationRequester.Rule("surface-invoices", "2"),
            isFlagged);

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        await context.WriteSession.Received(1).SetFlaggedAsync(
            occurrence,
            isFlagged,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
        await context.WriteSession.DidNotReceive().SetSeenAsync(
            Arg.Any<EmailOccurrenceId>(),
            Arg.Any<bool>(),
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Clearing the flag is the same mutation as setting it, and the direction reaches the session as asked.</summary>
    /// <remarks>
    /// Both directions are one authored act about one flag, so a request that asks for mail to be marked unread must not
    /// arrive at the server as a request to mark it read. The performer carries the direction off the record rather than
    /// deciding it, which is what makes a resumed attempt ask for what was originally asked for.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PerformAsync_ForASeenStateChange_CarriesTheAuthoredDirectionIntoTheSession(bool isSeen)
    {
        // Arrange
        var context = new PerformerContext();
        var occurrence = Occurrence(42U);
        var request = MailboxMutationRequest.SetSeen(
            StoredEmailId.Create(Guid.CreateVersion7()), SyntheticMailOwner.Deployment,
            occurrence,
            MailboxMutationRequester.Rule("surface-invoices", "2"),
            isSeen);

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        await context.WriteSession.Received(1).SetSeenAsync(
            occurrence,
            isSeen,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A caller's mistake about which binding the occurrence belongs to costs no login and reaches no mailbox.</summary>
    [Fact]
    public async Task PerformAsync_FolderBindingThatDoesNotCarryTheOccurrence_IsRefusedBeforeAnythingIsWrittenDown()
    {
        // Arrange
        var context = new PerformerContext();
        var otherFolder = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create("drafts"),
            RemoteFolderPath.Create("Drafts", '/'));

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => context.Performer.PerformAsync(
                RelocationRequest(),
                otherFolder,
                TransportPolicy,
                CancellationToken.None));

        // Assert
        Assert.Equal(0, context.Store.OpenedRecordCount);
    }

    /// <summary>Every change MailFathom is permitted to make leaves one entry behind on an account that asked for a trail.</summary>
    [Theory]
    [InlineData("relocate")]
    [InlineData("delete")]
    [InlineData("set-seen")]
    [InlineData("copy")]
    public async Task PerformAsync_OnAnAuditedAccount_LeavesOneEntryPerFinishedMutation(string mutationName)
    {
        // Arrange
        var context = new PerformerContext();
        context.Store.AuditsMutations = true;
        var request = RequestFor(mutationName);

        // Act
        await context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None);

        // Assert
        var entry = Assert.Single(context.AuditTrail.Entries);
        Assert.Equal(
            (request.Mutation, request.StoredEmailId, InboxFolder.RemotePath, MailboxMutationAuditOutcome.Performed),
            (entry.Mutation, entry.StoredEmailId, entry.SourceFolderPath, entry.Outcome));
    }

    /// <summary>An account that never asked for a history accumulates none, which is what off by default has to mean.</summary>
    [Fact]
    public async Task PerformAsync_OnAnAccountWithoutTheTrail_LeavesNothingBehind()
    {
        // Arrange
        var context = new PerformerContext();

        // Act
        await context.Performer.PerformAsync(
            RelocationRequest(),
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        Assert.Empty(context.AuditTrail.Entries);
    }

    /// <summary>A change nothing will attempt again is recorded as given up on, with the code it was given up on for.</summary>
    [Fact]
    public async Task PerformAsync_MutationTheServerRefuses_RecordsTheEndingItWasGivenUpOnFor()
    {
        // Arrange
        var context = new PerformerContext();
        context.Store.AuditsMutations = true;
        context.FailRelocationWith(new MailboxDestinationFolderMissingException(
            Account.Id,
            InboxFolder.Alias,
            MailboxMutation.Relocate,
            new InvalidOperationException("The folder could not be found.")));

        // Act
        await Assert.ThrowsAsync<MailboxDestinationFolderMissingException>(
            () => context.Performer.PerformAsync(
                RelocationRequest(),
                InboxFolder,
                TransportPolicy,
                CancellationToken.None));

        // Assert
        var entry = Assert.Single(context.AuditTrail.Entries);
        Assert.Equal(
            (MailboxMutationAuditOutcome.Abandoned,
                (MailFathomErrorCode?)MailFathomErrorCode.MailboxMutationDestinationMissing),
            (entry.Outcome, entry.Failure));
    }

    /// <summary>A trail that cannot be written costs the history and never the change that had already been made.</summary>
    [Fact]
    public async Task PerformAsync_TrailThatRefusesEveryAppend_StillPerformsTheMutation()
    {
        // Arrange
        var context = new PerformerContext();
        context.Store.AuditsMutations = true;
        context.AuditTrail.FailsEveryAppend = true;
        var request = RelocationRequest();

        // Act
        var outcome = await context.Performer.PerformAsync(
            request,
            InboxFolder,
            TransportPolicy,
            CancellationToken.None);

        // Assert
        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        Assert.Equal(MailboxMutationStage.Completed, context.Store.RecordOf(request).Stage);
    }

    /// <summary>The answer is resolved when the change is written down, so switching the trail on later leaves it out.</summary>
    [Fact]
    public async Task PerformAsync_TrailSwitchedOnAfterTheRecordWasOpened_LeavesThatMutationOut()
    {
        // Arrange
        var context = new PerformerContext(maximumAttempts: 3);
        var request = RelocationRequest();
        context.FailRelocationWith(
            new MailboxUnavailableException(Account.Id, InboxFolder.Alias, new TimeoutException()));

        await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None));

        // Act
        context.Store.AuditsMutations = true;
        context.AnswerRelocationWith(ArchivedAt);
        await context.Performer.PerformAsync(request, InboxFolder, TransportPolicy, CancellationToken.None);

        // Assert
        Assert.Empty(context.AuditTrail.Entries);
    }

    private static MailboxMutationRequest RequestFor(string mutationName)
    {
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var occurrence = Occurrence(42U);
        var requester = MailboxMutationRequester.Rule("file-newsletters", "3");

        return mutationName switch
        {
            "relocate" => MailboxMutationRequest.Relocate(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, ArchivePath),
            "copy" => MailboxMutationRequest.Copy(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, ArchivePath),
            "set-seen" => MailboxMutationRequest.SetSeen(storedEmailId, SyntheticMailOwner.Deployment, occurrence, requester, isSeen: true),
            _ => MailboxMutationRequest.Delete(
                storedEmailId, SyntheticMailOwner.Deployment,
                occurrence,
                requester,
                AuthoredDeleteEmailDisposition.RetainLocalCopy),
        };
    }

    private static EmailOccurrenceId Occurrence(uint uid) => EmailOccurrenceId.Create(
        Account.Id,
        InboxFolder.Id,
        ImapUidValidity.Create(7U),
        ImapUid.Create(uid));

    private static MailboxMutationRequest RelocationRequest() => MailboxMutationRequest.Relocate(
        StoredEmailId.Create(Guid.CreateVersion7()), SyntheticMailOwner.Deployment,
        Occurrence(42U),
        MailboxMutationRequester.Rule("file-newsletters", "3"),
        ArchivePath);

    /// <summary>Assembles the performer over an in-memory record store and a substituted write session.</summary>
    private sealed class PerformerContext
    {
        internal PerformerContext(int maximumAttempts = 5)
        {
            var persistenceSession = Substitute.For<IPersistenceSession>();
            persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

            this.WriteSession = Substitute.For<IMailboxWriteSession>();
            this.WriteSession.RelocateAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .Returns(RemoteEmailPlacement.NotReported());
            this.WriteSession.CopyAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .Returns(RemoteEmailPlacement.NotReported());

            this.WriteSessionFactory = Substitute.For<IMailboxWriteSessionFactory>();
            this.WriteSessionFactory.OpenForWritingAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderResolution>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(this.WriteSession);

            this.Performer = new MailboxMutationPerformer(
                this.Store,
                this.WriteSessionFactory,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                    TimeProvider.System),
                this.AuditTrail,
                new MailboxMutationOptions { MaximumAttempts = maximumAttempts });
        }

        internal InMemoryMailboxMutationRecordStore Store { get; } = new();

        internal RecordingMailboxMutationAuditTrail AuditTrail { get; } = new();

        internal IMailboxWriteSession WriteSession { get; }

        internal IMailboxWriteSessionFactory WriteSessionFactory { get; }

        internal MailboxMutationPerformer Performer { get; }

        internal void AnswerRelocationWith(RemoteEmailPlacement placement) =>
            this.WriteSession.RelocateAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .Returns(placement);

        internal void FailRelocationWith(Exception failure) =>
            this.WriteSession.RelocateAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(failure);

        /// <summary>Writes the record down without performing anything, so a test can arrange the state a crash left.</summary>
        internal async Task OpenRecordFor(MailboxMutationRequest request)
        {
            var session = Substitute.For<IPersistenceSession>();
            await this.Store.OpenAsync(session, request, CancellationToken.None);
        }
    }
}
