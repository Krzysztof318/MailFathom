// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations;

/// <summary>Covers the one place a change's executor is chosen, and what a held account's local commit does for each kind of change.</summary>
public sealed class MailboxChangeSubmissionTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("personal");

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("Archive");

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Copy = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly ReadOnlyMemory<byte> RawMime = "Subject: copied\r\n\r\nbody"u8.ToArray();

    private static readonly RemoteEmailKeywords CarriedKeywords = RemoteEmailKeywords.Create(["$label1"]);

    private static readonly EmailOccurrenceId Occurrence =
        EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(1), ImapUid.Create(41));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset Now = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryLocalMailFolderStore heldFolders = new(Account, MailAccountCustodyPhase.Held);

    private readonly InMemoryLocalEmailStateStore states = new(Account);

    private readonly IMailboxMutationAuditEntryStore auditEntries = Substitute.For<IMailboxMutationAuditEntryStore>();

    private readonly IPersistenceSession session = Substitute.For<IPersistenceSession>();

    private readonly IEmailMetadataRepository copies = Substitute.For<IEmailMetadataRepository>();

    /// <summary>An account whose source is the truth keeps the record it always had, so nothing about it changes.</summary>
    [Fact]
    public async Task SubmitAsync_OnAnAccountThatIsNotHeld_WritesTheDurableRecord()
    {
        // Arrange
        var submission = MailboxChangeSubmissions.Over(this.records, states: this.states);

        // Act
        var submitted = await submission.SubmitAsync(this.session, SeenRequest(isSeen: true), null, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Equal(1, this.records.OpenedRecordCount);
        Assert.Empty(this.states.States);
    }

    /// <summary>Whether a rule's move is worth carrying is the rule's decision, so the submission records what it is handed.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveIntoTheFolderAMirroredMessageIsIn_RecordsWhatItWasHanded()
    {
        // Arrange
        var submission = MailboxChangeSubmissions.Over(this.records);
        var destination = new MailboxDestination(Inbox, IsMirrored: true);

        // Act
        var submitted = await submission.SubmitAsync(this.session, MoveRequest(Inbox.RemotePath), destination, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>A person moving a mirrored message into the folder it is in is told it is already there, and nothing is recorded.</summary>
    [Fact]
    public async Task SubmitMoveAsync_AMoveIntoTheFolderAMirroredMessageIsIn_AnswersAlreadyThereWithoutARecord()
    {
        // Arrange
        var submission = MailboxChangeSubmissions.Over(this.records);
        var destination = new MailboxDestination(Inbox, IsMirrored: true);

        // Act
        var submitted = await submission.SubmitMoveAsync(this.session, MoveRequest(Inbox.RemotePath), destination, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.AlreadyInDestination, submitted.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A held account commits a read change at once, writes no record, and says what a client is to be told.</summary>
    [Fact]
    public async Task SubmitAsync_ASeenChangeOnAHeldAccount_CommitsItWithoutARecord()
    {
        // Arrange
        this.Store();

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, SeenRequest(isSeen: true), null, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.True(this.states.States[Email].IsSeen);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(new SignalledEmailFlags(Email, true, null), submitted.Change!.Flags);
    }

    /// <summary>The star is its own column and its own change, and moves nothing else.</summary>
    [Fact]
    public async Task SubmitAsync_AFlaggedChangeOnAHeldAccount_SetsTheStarAlone()
    {
        // Arrange
        this.Store();
        var request = MailboxMutationRequest.SetFlagged(Email, Occurrence, Requester, isFlagged: true);

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, request, null, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal((true, false), (this.states.States[Email].IsFlagged, this.states.States[Email].IsSeen));
    }

    /// <summary>Each keyword direction leaves the set the server would have left, in the normalized form a keyword filter matches on.</summary>
    [Theory]
    [InlineData("add", "urgent", "$LABEL1,URGENT,WORK")]
    [InlineData("remove", "work", "$LABEL1")]
    [InlineData("set", "home", "HOME")]
    public async Task SubmitAsync_AKeywordChangeOnAHeldAccount_LeavesTheSetTheDirectionNames(
        string direction,
        string keyword,
        string expected)
    {
        // Arrange
        this.Store(keywords: ["WORK", "$Label1"]);
        var named = AuthoredMailKeywords.Create([keyword]);
        var request = direction switch
        {
            "add" => MailboxMutationRequest.AddKeywords(Email, Occurrence, Requester, named),
            "remove" => MailboxMutationRequest.RemoveKeywords(Email, Occurrence, Requester, named),
            _ => MailboxMutationRequest.SetKeywords(Email, Occurrence, Requester, named),
        };

        // Act
        await this.Held().SubmitAsync(this.session, request, null, null, Token);

        // Assert
        Assert.Equal(expected.Split(','), this.states.States[Email].Keywords.Values);
    }

    /// <summary>A destination named by its role files into that role's local folder, which exists even before anything arrived in it.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveToARoleOnAHeldAccount_FilesIntoThatRolesLocalFolder()
    {
        // Arrange
        this.Store();
        var junk = JunkDestination();

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, MoveRequest(junk.Path), junk, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal(this.FolderWithRole(MailFolderSpecialUse.Junk), this.states.States[Email].Folder);
    }

    /// <summary>Any other destination is the local folder its source folder created, which is the correspondence arrivals follow.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveToAnAliasOnAHeldAccount_FilesIntoTheLocalFolderThatSourceCreated()
    {
        // Arrange
        this.Store();
        var archive = new LocalMailFolder(
            LocalMailFolderId.Create(Guid.CreateVersion7()),
            ParentId: null,
            LocalMailFolderName.Create("Archive"),
            Role: null,
            SourceFolderAlias: Archive);
        await this.heldFolders.SaveAsync(this.session, Account, [archive], [], Token);
        var destination = new MailboxDestination(
            MailFolderResolution.FirstBindingOf(Archive, RemoteFolderPath.Create("Archive")),
            IsMirrored: true);

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, MoveRequest(destination.Path), destination, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal(archive.Id, this.states.States[Email].Folder);
    }

    /// <summary>A source folder that has sent nothing yet has no local folder, and nothing is invented to file into.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveToAnAliasNoLocalFolderCorrespondsTo_ReportsTheDestinationMissing()
    {
        // Arrange
        this.Store();
        var destination = new MailboxDestination(
            MailFolderResolution.FirstBindingOf(Archive, RemoteFolderPath.Create("Archive")),
            IsMirrored: true);

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, MoveRequest(destination.Path), destination, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.DestinationMissing, submitted.Outcome);
        Assert.Null(this.states.States[Email].Folder);
    }

    /// <summary>On a held account "already there" is a question about the local folder, not the alias the message was stored under.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveIntoTheLocalFolderAHeldMessageIsIn_WritesNothing()
    {
        // Arrange
        this.Store();
        var junk = JunkDestination();
        var submission = this.Held();
        await submission.SubmitAsync(this.session, MoveRequest(junk.Path), junk, null, Token);

        // Act
        var submitted = await submission.SubmitAsync(this.session, MoveRequest(junk.Path), junk, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.AlreadyInDestination, submitted.Outcome);
    }

    /// <summary>Delete means the trash, and a message moved there is still somebody's to take back out.</summary>
    [Fact]
    public async Task SubmitAsync_ADeleteOnAHeldAccount_MovesTheMessageIntoTheTrash()
    {
        // Arrange
        this.Store();

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, DeleteRequest(), null, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal(this.FolderWithRole(MailFolderSpecialUse.Trash), this.states.States[Email].Folder);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A delete inside the trash is an erasure, and its record is what holds it for the window the withdrawal routes act on.</summary>
    [Fact]
    public async Task SubmitAsync_ADeleteOfAHeldMessageAlreadyInTheTrash_OpensAnErasureHeldForTheWindow()
    {
        // Arrange
        this.Store();
        var submission = this.Held();
        await submission.SubmitAsync(this.session, DeleteRequest(), null, null, Token);
        var window = Now.AddSeconds(30);

        // Act
        var submitted = await submission.SubmitAsync(this.session, DeleteRequest(), null, window, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Equal(window, this.records.HeldUntilOf(DeleteRequest()));
        Assert.Contains(Email, this.states.States.Keys);
    }

    /// <summary>A rule meeting its own earlier delete in the trash is not a person's second delete, so it erases nothing and records nothing.</summary>
    [Fact]
    public async Task SubmitAsync_ARuleDeletingAHeldMessageAlreadyInTheTrash_LeavesItThereWithoutARecord()
    {
        // Arrange
        this.Store();
        var submission = this.Held();
        var ruleDelete = MailboxMutationRequest.Delete(
            Email,
            Occurrence,
            MailboxMutationRequester.Rule("tidy-newsletters", "revision-1"),
            AuthoredDeleteEmailDisposition.RetainLocalCopy,
            AuthoredDeleteServerDisposition.Expunge);
        await submission.SubmitAsync(this.session, ruleDelete, null, null, Token);

        // Act
        var submitted = await submission.SubmitAsync(this.session, ruleDelete, null, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.AlreadyInDestination, submitted.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(this.FolderWithRole(MailFolderSpecialUse.Trash), this.states.States[Email].Folder);
    }

    /// <summary>A copy is a second stored message, filed where it was copied to and leaving the copied one alone.</summary>
    [Fact]
    public async Task SubmitAsync_ACopyOnAHeldAccount_CommitsASecondStoredMessage()
    {
        // Arrange
        this.Store();
        var junk = JunkDestination();
        var prepared = await this.PrepareCopyAsync();

        // Act
        var submitted = await this.Held(copier: this.Copying())
            .SubmitAsync(this.session, CopyRequest(junk), junk, null, prepared, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal(Copy, submitted.Change!.Email);
        Assert.Equal(this.FolderWithRole(MailFolderSpecialUse.Junk), this.heldFolders.Placements[Copy]);
        Assert.Null(this.states.States[Email].Folder);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>The copy is the copied message's state as well as its content, so it carries the flags and the keywords.</summary>
    [Fact]
    public async Task SubmitAsync_ACopyOnAHeldAccount_CarriesTheCopiedMessagesFlagsAndKeywords()
    {
        // Arrange
        this.states.Store(
            Email,
            new LocalEmailState(Inbox, Folder: null, IsSeen: true, IsFlagged: true, CarriedKeywords));
        var junk = JunkDestination();
        var prepared = await this.PrepareCopyAsync();

        // Act
        await this.Held(copier: this.Copying())
            .SubmitAsync(this.session, CopyRequest(junk), junk, null, prepared, Token);

        // Assert
        await this.copies.Received(1).StoreLocalCopyAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Inbox.Id,
            Arg.Any<ExtractedEmailMetadata?>(),
            Arg.Any<long>(),
            Arg.Is<CopiedMailFlags>(flags =>
                flags.IsSeen && flags.IsFlagged && flags.Keywords == CarriedKeywords),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A copy needs the message itself, so one whose payload the account does not store is refused.</summary>
    [Fact]
    public async Task SubmitAsync_ACopyWhosePayloadIsNotStored_ReportsTheContentMissing()
    {
        // Arrange
        this.Store();
        var junk = JunkDestination();

        // Act
        var submitted = await this.Held().SubmitAsync(this.session, CopyRequest(junk), junk, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.SourceContentMissing, submitted.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Empty(this.heldFolders.Placements);
    }

    /// <summary>Copying a message into the folder it is already in is the nothing a move into it is.</summary>
    [Fact]
    public async Task SubmitAsync_ACopyIntoTheLocalFolderTheMessageIsIn_AnswersAlreadyThere()
    {
        // Arrange
        this.Store();
        var junk = JunkDestination();
        await this.Held().SubmitAsync(this.session, MoveRequest(junk.Path), junk, null, Token);
        var prepared = await this.PrepareCopyAsync();

        // Act
        var submitted = await this.Held(copier: this.Copying())
            .SubmitAsync(this.session, CopyRequest(junk), junk, null, prepared, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.AlreadyInDestination, submitted.Outcome);
        Assert.DoesNotContain(Copy, this.heldFolders.Placements.Keys);
    }

    /// <summary>A destination no local folder corresponds to is nowhere to file a copy, exactly as it is nowhere to move one.</summary>
    [Fact]
    public async Task SubmitAsync_ACopyIntoADestinationWithNoLocalFolder_ReportsTheDestinationMissing()
    {
        // Arrange
        this.Store();
        var unheld = new MailboxDestination(
            MailFolderResolution.FirstBindingOf(Archive, RemoteFolderPath.Create("Archive")),
            IsMirrored: true);
        var prepared = await this.PrepareCopyAsync();

        // Act
        var submitted = await this.Held(copier: this.Copying())
            .SubmitAsync(this.session, CopyRequest(unheld), unheld, null, prepared, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.DestinationMissing, submitted.Outcome);
        Assert.Empty(this.heldFolders.Placements);
    }

    /// <summary>An account whose source is still the truth copies through a record, so nothing is placed for it.</summary>
    [Fact]
    public async Task PrepareCopiesAsync_OnAnAccountThatIsNotHeld_PlacesNothing()
    {
        // Arrange
        var submission = MailboxChangeSubmissions.Over(this.records, copier: this.Copying());

        // Act
        var prepared = await submission.PrepareCopiesAsync(Account, [Email], Token);

        // Assert
        Assert.Empty(prepared.Of(Email));
    }

    /// <summary>Two rules copying one message produce two stored messages, so two payloads are placed for it.</summary>
    [Fact]
    public async Task PrepareCopiesAsync_ForAMessageCopiedTwice_PlacesAPayloadPerCopy()
    {
        // Act
        var prepared = await this.Held(copier: this.Copying()).PrepareCopiesAsync(Account, [Email, Email], Token);

        // Assert
        Assert.Equal(2, prepared.Of(Email).Count);
    }

    /// <summary>A message the held account no longer stores has nothing to change.</summary>
    [Fact]
    public async Task SubmitAsync_AMessageAHeldAccountNoLongerStores_ReportsItMissing()
    {
        // Act
        var submitted = await this.Held().SubmitAsync(this.session, SeenRequest(isSeen: true), null, null, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.MessageMissing, submitted.Outcome);
    }

    /// <summary>An account keeping a trail gets the same entry for a local act that a remote change leaves, in the same transaction.</summary>
    [Fact]
    public async Task SubmitAsync_AnAuditedHeldAccount_AppendsThePerformedActInTheSameSession()
    {
        // Arrange
        this.Store();
        var auditSettings = Substitute.For<IMailboxMutationAuditSettingsReader>();
        auditSettings
            .GetAuditSettings(Account)
            .Returns(new MailboxMutationAuditSettings(IsEnabled: true, TimeSpan.FromDays(30)));

        // Act
        await this.Held(auditSettings).SubmitAsync(this.session, SeenRequest(isSeen: true), null, null, Token);

        // Assert
        await this.auditEntries.Received(1).AppendAsync(
            this.session,
            Arg.Is<MailboxMutationAuditEntry>(entry =>
                entry != null
                && entry.Mutation == MailboxMutation.SetSeen
                && entry.StoredEmailId == Email
                && entry.Outcome == MailboxMutationAuditOutcome.Performed
                && entry.CompletedAt == Now),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account keeping no trail gets no entry, exactly as its remote changes get none.</summary>
    [Fact]
    public async Task SubmitAsync_AnUnauditedHeldAccount_AppendsNothing()
    {
        // Arrange
        this.Store();

        // Act
        await this.Held().SubmitAsync(this.session, SeenRequest(isSeen: true), null, null, Token);

        // Assert
        await this.auditEntries.DidNotReceiveWithAnyArgs().AppendAsync(default!, default!, Token);
    }

    /// <summary>An erasure whose window passed removes the message and leaves the entry naming the record a person could have withdrawn.</summary>
    [Fact]
    public async Task EraseAsync_AnAuditedErasure_RemovesTheMessageAndAppendsItsCompletion()
    {
        // Arrange
        this.Store();
        this.records.AuditsMutations = true;
        var record = await this.records.OpenAsync(this.session, DeleteRequest(), heldUntil: null, Token);

        // Act
        var erased = await this.Held().EraseAsync(this.session, record, Token);

        // Assert
        Assert.NotNull(erased);
        Assert.Equal(Email, Assert.Single(this.states.Erased));
        await this.auditEntries.Received(1).AppendAsync(
            this.session,
            Arg.Is<MailboxMutationAuditEntry>(entry =>
                entry != null && entry.MutationRecordId == record.Id && entry.Outcome == MailboxMutationAuditOutcome.Performed),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A person who withdrew the delete after the pass read its record keeps the message: the erasure claims the record, and a cancelled one refuses.</summary>
    [Fact]
    public async Task EraseAsync_ARecordWithdrawnAfterThePassReadIt_ErasesNothing()
    {
        // Arrange
        this.Store();
        var submission = this.Held();
        await submission.SubmitAsync(this.session, DeleteRequest(), null, null, Token);
        var submitted = await submission.SubmitAsync(this.session, DeleteRequest(), null, Now.AddSeconds(30), Token);
        var readByThePass = submitted.Record!;
        await this.records.WithdrawAsync(this.session, [Account], [readByThePass.Id], Token);

        // Act
        var erasure = submission.EraseAsync(this.session, readByThePass, Token);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => erasure);
        Assert.Contains(Email, this.states.States.Keys);
    }

    /// <summary>A committed flag change reaches the clients watching the account as a flag change, which they apply without a re-read.</summary>
    [Fact]
    public async Task Announce_ACommittedFlagChange_PublishesItToTheAccountsClients()
    {
        // Arrange
        var channel = Substitute.For<IClientSignalChannel>();
        var change = new AppliedMailboxChange(Account, Inbox.Alias, Email, new SignalledEmailFlags(Email, true, null));

        // Act
        await using (var signals = new ClientSignals([channel], new FakeTimeProvider(Now)))
        {
            this.Held(signals: signals).Announce(change);
        }

        // Assert
        await channel.Received(1).PublishAsync(
            Arg.Is<ClientSignal>(signal =>
                signal != null
                && signal.Kind == ClientSignalKind.MailFlagsChanged
                && signal.Folder == Inbox.Alias
                && signal.Flags.SequenceEqual(new[] { new SignalledEmailFlags(Email, true, null) })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A committed move changes where the row is drawn, so clients are told to re-read rather than to apply a flag.</summary>
    [Fact]
    public async Task Announce_ACommittedMove_PublishesAMailChangeNamingTheMessage()
    {
        // Arrange
        var channel = Substitute.For<IClientSignalChannel>();
        var change = new AppliedMailboxChange(Account, Inbox.Alias, Email, Flags: null);

        // Act
        await using (var signals = new ClientSignals([channel], new FakeTimeProvider(Now)))
        {
            this.Held(signals: signals).Announce(change);
        }

        // Assert
        await channel.Received(1).PublishAsync(
            Arg.Is<ClientSignal>(signal =>
                signal != null
                && signal.Kind == ClientSignalKind.MailChanged
                && signal.Folder == Inbox.Alias
                && signal.Emails.SequenceEqual(new[] { Email })),
            Arg.Any<CancellationToken>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MailboxMutationRequest SeenRequest(bool isSeen) =>
        MailboxMutationRequest.SetSeen(Email, Occurrence, Requester, isSeen);

    private static MailboxMutationRequest MoveRequest(RemoteFolderPath destination) =>
        MailboxMutationRequest.Relocate(Email, Occurrence, Requester, destination);

    private static MailboxMutationRequest DeleteRequest() => MailboxMutationRequest.Delete(
        Email,
        Occurrence,
        Requester,
        AuthoredDeleteEmailDisposition.RetainLocalCopy,
        AuthoredDeleteServerDisposition.Expunge);

    private static MailboxMutationRequest CopyRequest(MailboxDestination destination) =>
        MailboxMutationRequest.Copy(Email, Occurrence, Requester, destination.Path);

    /// <summary>Builds a content store that answers the copied message's payload and places what it is handed.</summary>
    private static IEmailContentStore StoredContentFound()
    {
        var contents = ContentStores.Substituted();

        contents
            .FindStoredContentAsync(Email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(new StoredEmailContent(
                RawMime,
                RawMime.Length,
                ReadOnlyMemory<byte>.Empty)));

        return contents;
    }

    /// <summary>Builds a MIME reader that reads no metadata, which is the envelope-only path a copy may take.</summary>
    private static IEmailMimeReader MimeReadingNothing()
    {
        var mimeReader = Substitute.For<IEmailMimeReader>();

        mimeReader
            .ReadMetadataAsync(Arg.Any<MailAccountId>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.MalformedContent()));

        return mimeReader;
    }

    private static MailboxDestination JunkDestination() => new(
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("Junk"), RemoteFolderPath.Create("Junk")),
        IsMirrored: true,
        MailFolderSpecialUse.Junk);

    private MailboxChangeSubmission Held(
        IMailboxMutationAuditSettingsReader? auditSettings = null,
        ClientSignals? signals = null,
        LocalMailCopier? copier = null) => MailboxChangeSubmissions.Over(
        this.records,
        this.heldFolders,
        this.states,
        copier ?? LocalMailCopiers.Over(this.heldFolders),
        this.auditEntries,
        auditSettings,
        signals,
        new FakeTimeProvider(Now));

    /// <summary>Builds a copier that finds the copied message's payload and writes the copy as <see cref="Copy" />.</summary>
    private LocalMailCopier Copying() => LocalMailCopiers.Over(
        this.heldFolders,
        StoredContentFound(),
        this.copies,
        MimeReadingNothing());

    private void Store(IEnumerable<string>? keywords = null) => this.states.Store(
        Email,
        new LocalEmailState(Inbox, Folder: null, IsSeen: false, IsFlagged: false, RemoteEmailKeywords.Create(keywords ?? [])));

    private LocalMailFolderId FolderWithRole(MailFolderSpecialUse role) =>
        this.heldFolders.Folders.Single(folder => folder.Role == role).Id;

    /// <summary>Places the payload one copy is committed from, which is what a caller does before its transaction.</summary>
    private async Task<PreparedLocalCopy> PrepareCopyAsync()
    {
        this.copies
            .StoreLocalCopyAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ExtractedEmailMetadata?>(),
                Arg.Any<long>(),
                Arg.Any<CopiedMailFlags>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailId?>(Copy));

        return Assert.Single(
            (await this.Held(copier: this.Copying()).PrepareCopiesAsync(Account, [Email], Token)).Of(Email));
    }
}
