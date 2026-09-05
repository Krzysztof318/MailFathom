// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers the moving grant, the visibility rule the destination is judged by, and the record a move is written down as.</summary>
/// <remarks>
/// Every refusal here is a result rather than an exception, because a caller moving several messages at once reads one
/// answer per message and carries on. The one exception is the grant, which is the whole request's outcome.
/// </remarks>
public sealed class MailRelocationRecorderTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("Archive");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TlsOnConnect = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryMailFolderResolutionStore bindings = new();

    private readonly StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;

    private readonly List<RemoteFolder> advertisedFolders = [];

    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    /// <summary>Arranges the disposition every test shares, so a test that is about it overrides it afterwards.</summary>
    public MailRelocationRecorderTests() => this.dispositions
        .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
        .Returns(AuthoredDeleteEmailDisposition.RetainLocalCopy);

    /// <summary>The record names the occurrence and the remote path a command will be issued against, neither of which the caller supplied.</summary>
    [Fact]
    public async Task RecordAsync_AMoveIntoAMirroredFolder_OpensOneRecordAgainstTheOccurrence()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive/2026");
        var target = TargetIn(Inbox);
        var recorder = this.Recorder(target);

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Archive,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.Recorded, result.Outcome);
        Assert.Equal(Archive, result.Destination);
        Assert.Equal(MailboxMutationLifecycle.Pending, result.Lifecycle);

        var request = Assert.Single(this.records.OpenedRequests);

        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal(target.Occurrence, request.Occurrence);
        Assert.Equal(RemoteFolderPath.Create("Archive/2026"), request.DestinationPath);
    }

    /// <summary>A mirrored destination keeps the local copy the mirror already carries, so the record names no disposition.</summary>
    [Fact]
    public async Task RecordAsync_AMoveIntoAMirroredFolder_NamesNoLocalDisposition()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        await recorder.RecordAsync(LocalEmail, Archive, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(this.records.OpenedRequests).LocalDisposition);
        this.dispositions.DidNotReceive().GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>());
    }

    /// <summary>
    /// Mail moved into a folder nothing mirrors leaves the mirror for good, so what becomes of the local copy is the
    /// account's own answer and is written onto the record while the owner's configuration still says it.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AMoveIntoAnUnmirroredFolder_RecordsWhatBecomesOfTheLocalCopy()
    {
        // Arrange
        this.MapUnmirrored(Archive, "Archive");
        this.dispositions
            .GetAuthoredDeleteDisposition(Account.Id)
            .Returns(AuthoredDeleteEmailDisposition.EraseLocalCopy);
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Archive,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.Recorded, result.Outcome);
        Assert.Equal(
            AuthoredDeleteEmailDisposition.EraseLocalCopy,
            Assert.Single(this.records.OpenedRequests).LocalDisposition);
    }

    /// <summary>
    /// Moving mail is its own grant, so a caller holding the one that writes flags is refused. A flag misdescribes mail
    /// the owner can still find; a move puts the mail somewhere else.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ACallerHoldingOnlyTheFlagGrant_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(
            TargetIn(Inbox),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(LocalEmail, Archive, Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailMove, refusal.RequiredPermission);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder no tool may read is a folder no tool may move mail out of, or the write surface would be the way round the withholding.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromTools_ReportsTheMessageAsNotFound()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Withheld));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Archive,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.MessageNotFound, result.Outcome);
        Assert.Null(result.RecordId);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>An email no row carries answers exactly as a withheld one does, so asking cannot reveal which identifiers exist.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailThisDeploymentHoldsNoRowFor_ReportsTheMessageAsNotFound()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(target: null);

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Archive,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.MessageNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// A destination withheld from tools is refused as a destination that is not there, because filing mail into a
    /// folder the caller cannot read would move it out of sight rather than be a capability of its own.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADestinationWithheldFromTools_ReportsTheDestinationAsNotFound()
    {
        // Arrange
        this.MapMirrored(Withheld, "Private");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Withheld,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.DestinationNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A name this deployment maps no folder to reaches the same refusal as one it withholds, and writes nothing either way.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationNoMappingNames_ReportsTheDestinationAsNotFound()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Archive,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.DestinationNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A move to where the message already is is nothing to carry out, and a record would be a command the server refuses.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationTheEmailIsAlreadyIn_ReportsItAsAlreadyThere()
    {
        // Arrange
        this.MapMirrored(Inbox, "INBOX");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Inbox,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.AlreadyInDestination, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// An account removed from configuration between the read and the write leaves the disposition unanswerable, which
    /// is this caller's message to report rather than a fault that ends the batch.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnAccountThatLeftConfigurationMidRequest_ReportsItRatherThanFailing()
    {
        // Arrange
        this.MapUnmirrored(Archive, "Archive");
        this.dispositions
            .GetAuthoredDeleteDisposition(Account.Id)
            .Throws(new InvalidOperationException("No account carries the identifier personal."));
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Archive,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.AccountNoLongerConfigured, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A retry under one identity is one move, because the record store admits one per occurrence, requester, and mutation.</summary>
    [Fact]
    public async Task RecordAsync_TheSameMoveAskedTwiceUnderOneRequester_OpensOneRecord()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var first = await recorder.RecordAsync(LocalEmail, Archive, Requester, TestContext.Current.CancellationToken);
        var second = await recorder.RecordAsync(LocalEmail, Archive, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.records.OpenedRecordCount);
        Assert.Equal(first.RecordId, second.RecordId);
    }

    /// <summary>A move with no folder named is a caller fault rather than an outcome, because there is nothing to report an answer about.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationNamingNoFolder_IsRefused()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(() =>
            recorder.RecordAsync(LocalEmail, default, Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("destination", thrown.ParamName);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>Maps a folder the account mirrors, and records the binding its own synchronization run would have.</summary>
    private void MapMirrored(MailFolderAlias alias, string remotePath)
    {
        this.mappings.With(Account.Id, MailFolderMapping.ToRemotePath(alias, RemoteFolderPath.Create(remotePath)));
        this.bindings.Bind(Account.Id, alias, remotePath);
    }

    /// <summary>Maps a folder no run schedules, which is resolved against what the server advertises at the moment it is needed.</summary>
    private void MapUnmirrored(MailFolderAlias alias, string remotePath)
    {
        this.mappings.With(
            Account.Id,
            MailFolderMapping.ToRemotePath(
                alias,
                RemoteFolderPath.Create(remotePath),
                MailFolderParticipation.MappedOnly));
        this.advertisedFolders.Add(new RemoteFolder(RemoteFolderPath.Create(remotePath), []));
    }

    private MailRelocationRecorder Recorder(
        AuthoredMailboxTarget? target,
        AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove);
        var accountCatalog = OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account.Id));

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRelocationRecorder(
            callerAuthorization,
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation
                    .Mapping(
                        new MailFolderIdentity(Account.Id, Inbox),
                        new MailFolderIdentity(Account.Id, Archive))
                    .Hiding(new MailFolderIdentity(Account.Id, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            targets,
            this.DestinationResolver(sessions),
            this.dispositions,
            this.records,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(RecordedAt)));
    }

    private MailboxDestinationResolver DestinationResolver(IPersistenceSessionFactory sessionFactory)
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RemoteFolder>>([.. this.advertisedFolders]));

        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(TlsOnConnect);

        return new MailboxDestinationResolver(
            this.mappings.Resolver,
            this.bindings,
            new MailFolderResolver(
                remoteFolderCatalog,
                Substitute.For<IRemoteFolderCreator>(),
                this.bindings,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                sessionFactory,
                ClientSignalPublishers.ReachingNobody,
                new FakeTimeProvider(RecordedAt)),
            transportSecurityPolicies);
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var folder = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value));

        return new AuthoredMailboxTarget(
            Account.Owner,
            EmailOccurrenceId.Create(Account.Id, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
