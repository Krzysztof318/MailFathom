// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Results;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>set_mail_flags</c> tool itself owns: reading a call and naming the invocation asking.</summary>
/// <remarks>
/// <para>
/// The tool calls the real <see cref="MailFlagChangeRecorder" /> rather than a substitute for it, because the grant, the
/// visibility rule, and the one record per value are the use case's and a substitute would only prove that the tool
/// composes with a fiction. What the substitutes replace is the row the email is at and the durable record store.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: text a call is refused over never
/// reaches the use case, and no failure message repeats what the caller sent.
/// </para>
/// </remarks>
public sealed class SetMailFlagsToolTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SetMailFlagsAsync_EveryValueAsked_PublishesOneRecordPerValueInAFixedOrder()
    {
        // Arrange
        var storedEmailId = Guid.CreateVersion7();
        var tool = ToolOver(out _);

        // Act
        var result = await tool.SetMailFlagsAsync(
            storedEmailId.ToString(),
            seen: true,
            flagged: true,
            SetMailFlagsKeywordChange.Add,
            ["$Todo"],
            requestId: "triage-1",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(storedEmailId.ToString(), result.StoredEmailId);
        Assert.Equal(Account.Value, result.AccountId);
        Assert.Equal(Inbox.Value, result.FolderAlias);
        Assert.Equal(
            ["set-seen", "set-flagged", "add-keywords"],
            result.RecordedChanges.Select(recorded => recorded.Change));

        // Nothing has been issued to a mail server yet, which is the whole reason the result reports records.
        Assert.All(result.RecordedChanges, recorded => Assert.Equal("pending", recorded.State));
        Assert.All(result.RecordedChanges, recorded => Assert.True(Guid.TryParse(recorded.ChangeRecordId, out _)));
    }

    /// <summary>A value the call left out is a value no record is written for, which is what makes each one optional.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_OneValueAsked_WritesThatChangeAlone()
    {
        // Arrange
        var tool = ToolOver(out var records);

        // Act
        var result = await tool.SetMailFlagsAsync(
            Guid.CreateVersion7().ToString(),
            flagged: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var recorded = Assert.Single(result.RecordedChanges);
        Assert.Equal("set-flagged", recorded.Change);
        var request = Assert.Single(records.OpenedRequests);
        Assert.False(request.DesiredFlaggedState);
    }

    /// <summary>Text that names no email this system issued is refused where it arrives, before anything is looked up.</summary>
    [Theory]
    [InlineData("not-an-identifier")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public async Task SetMailFlagsAsync_TextNamingNoEmailThisSystemIssued_IsRefusedWithoutReachingTheUseCase(
        string storedEmailId)
    {
        // Arrange
        var tool = ToolOver(out var records, out var targets);

        // Act
        var refusal = await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(() =>
            tool.SetMailFlagsAsync(
                storedEmailId,
                seen: true,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.StoredEmailIdentifierMalformed, refusal.ErrorCode);
        Assert.Empty(records.OpenedRequests);
        await targets.DidNotReceive().FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The parse scans whatever it is handed, so the length is refused before anything reads an identity out of it.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_TextLongerThanAnyIdentifier_IsRefusedWithoutRepeatingIt()
    {
        // Arrange
        var overlongIdentifier = new string('a', 4096);
        var tool = ToolOver(out var records);

        // Act
        var refusal = await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(() =>
            tool.SetMailFlagsAsync(
                overlongIdentifier,
                seen: true,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain(overlongIdentifier, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(records.OpenedRequests);
    }

    /// <summary>A caller naming its request makes a retry the same request, which is what the record's identity is keyed to.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_ARequestIdentityTheCallerSent_RecordsTheChangeUnderIt()
    {
        // Arrange
        var tool = ToolOver(out var records);

        // Act
        await tool.SetMailFlagsAsync(
            Guid.CreateVersion7().ToString(),
            seen: true,
            requestId: " triage-42 ",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var requester = Assert.Single(records.OpenedRequests).Requester;
        Assert.Equal(MailboxMutationOrigin.Command, requester.Origin);
        Assert.Equal("triage-42", requester.Identity);
    }

    /// <summary>A call that declined to say whether it was a retry is a request of its own, or a star and an unstar would collapse.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_NoRequestIdentity_NamesEachCallItsOwnRequest()
    {
        // Arrange
        var storedEmailId = Guid.CreateVersion7().ToString();
        var tool = ToolOver(out var records);

        // Act
        await tool.SetMailFlagsAsync(
            storedEmailId,
            flagged: true,
            cancellationToken: TestContext.Current.CancellationToken);
        await tool.SetMailFlagsAsync(
            storedEmailId,
            flagged: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var identities = records.OpenedRequests.Select(request => request.Requester.Identity).ToArray();
        Assert.Equal(2, identities.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            records.OpenedRequests,
            request => Assert.Equal(MailboxMutationOrigin.Command, request.Requester.Origin));
    }

    /// <summary>An identity no record could be written under is refused about the field the caller sent rather than as an argument fault.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("triage\u0007one")]
    public async Task SetMailFlagsAsync_ARequestIdentityNoRecordCouldBeWrittenUnder_IsRefused(string requestId)
    {
        // Arrange
        var tool = ToolOver(out var records);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() =>
            tool.SetMailFlagsAsync(
                Guid.CreateVersion7().ToString(),
                seen: true,
                requestId: requestId,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
        Assert.Empty(records.OpenedRequests);
    }

    /// <summary>The bound the durable column carries is checked here, so a caller learns it named too long a value.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_ARequestIdentityLongerThanTheRecordAdmits_IsRefused()
    {
        // Arrange
        var overlongRequestId = new string('r', MailboxMutationRequester.MaximumIdentityLength + 1);
        var tool = ToolOver(out var records);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() =>
            tool.SetMailFlagsAsync(
                Guid.CreateVersion7().ToString(),
                seen: true,
                requestId: overlongRequestId,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain(overlongRequestId, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(records.OpenedRequests);
    }

    /// <summary>A call that named an email and asked for nothing is a client mistake, and it writes nothing down.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_NoValueAsked_IsRefused()
    {
        // Arrange
        var tool = ToolOver(out var records);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() =>
            tool.SetMailFlagsAsync(
                Guid.CreateVersion7().ToString(),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
        Assert.Empty(records.OpenedRequests);
    }

    /// <summary>The keyword list is bounded here, before anything normalizes and sorts what the caller sent.</summary>
    /// <remarks>
    /// The domain's own ceiling is compared against the deduplicated set, so a list naming one keyword many times would
    /// be expanded in full and then accepted. The count the caller sent is what this refuses, which is the only reading
    /// of the list that costs nothing.
    /// </remarks>
    [Fact]
    public async Task SetMailFlagsAsync_MoreKeywordsThanAMessageMayCarry_IsRefusedBeforeTheyAreRead()
    {
        // Arrange
        var repeatedKeyword = Enumerable
            .Repeat("$Todo", RemoteEmailKeywords.MaximumKeywords + 1)
            .ToArray();
        var tool = ToolOver(out var records);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() =>
            tool.SetMailFlagsAsync(
                Guid.CreateVersion7().ToString(),
                keywordChange: SetMailFlagsKeywordChange.Add,
                keywords: repeatedKeyword,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
        Assert.Empty(records.OpenedRequests);
    }

    /// <summary>A keyword direction outside the published set is the caller's own input and is refused as one.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_AKeywordDirectionThisSurfaceDoesNotPublish_IsRefused()
    {
        // Arrange
        var tool = ToolOver(out var records);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() =>
            tool.SetMailFlagsAsync(
                Guid.CreateVersion7().ToString(),
                keywordChange: (SetMailFlagsKeywordChange)99,
                keywords: ["$Todo"],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
        Assert.Empty(records.OpenedRequests);
    }

    /// <summary>Changing a mailbox is its own grant, asked for by the use case whatever entrypoint reached it.</summary>
    [Fact]
    public async Task SetMailFlagsAsync_ACallerHoldingOnlyTheReadingGrant_IsRefused()
    {
        // Arrange
        var tool = ToolOver(
            out var records,
            out _,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            tool.SetMailFlagsAsync(
                Guid.CreateVersion7().ToString(),
                seen: true,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailFlagsWrite, refusal.RequiredPermission);
        Assert.Empty(records.OpenedRequests);
    }

    private static SetMailFlagsTool ToolOver(out RecordingMailboxMutationRecordStore records) =>
        ToolOver(out records, out _);

    private static SetMailFlagsTool ToolOver(
        out RecordingMailboxMutationRecordStore records,
        out IAuthoredMailboxTargetReader targets,
        AccessAuthorization? authorization = null)
    {
        records = new RecordingMailboxMutationRecordStore();

        var folder = MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create("INBOX", '/'));
        targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AuthoredMailboxTarget?>(new AuthoredMailboxTarget(
                EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(9), ImapUid.Create(41)),
                folder)));

        var accountCatalog = Substitute.For<IMailAccountCatalog>();
        accountCatalog.ServedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new SetMailFlagsTool(new MailFlagChangeRecorder(
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation.Mapping(new MailFolderIdentity(Account, Inbox)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            targets,
            records,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider())));
    }

    /// <summary>A record store that keeps what a call asked to have written down.</summary>
    /// <remarks>
    /// Written out rather than substituted because every test here reads the requests back, and configuring a substitute
    /// to both answer and remember is longer than the two members this needs. The reads convergence performs are not
    /// part of what a tool call reaches, so they are refused rather than answered with a fiction.
    /// </remarks>
    private sealed class RecordingMailboxMutationRecordStore : IMailboxMutationRecordStore
    {
        private readonly List<MailboxMutationRequest> openedRequests = [];

        public IReadOnlyList<MailboxMutationRequest> OpenedRequests => this.openedRequests;

        public Task<MailboxMutationRecord> OpenAsync(
            IPersistenceSession session,
            MailboxMutationRequest request,
            CancellationToken cancellationToken)
        {
            this.openedRequests.Add(request);

            return Task.FromResult(new MailboxMutationRecord
            {
                Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(RecordedAt)),
                Request = request,
                Stage = MailboxMutationStage.Recorded,
                Placement = RemoteEmailPlacement.NotReported(),
                RequiresSourceRemoval = false,
                IsAudited = false,
                AttemptCount = 0,
                RecordedAt = RecordedAt,
                StageChangedAt = RecordedAt,
                LastFailure = null,
                PlacementObservedAt = null,
                SourceRemovalObservedAt = null,
            });
        }

        public Task<bool> HasRecordAsync(
            StoredEmailId storedEmailId,
            MailboxMutation mutation,
            MailboxMutationOrigin origin,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountAttemptAsync(
            IPersistenceSession session,
            MailboxMutationRecordId recordId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordPlacementIssuedAsync(
            IPersistenceSession session,
            MailboxMutationRecordId recordId,
            bool requiresSourceRemoval,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AdvanceAsync(
            IPersistenceSession session,
            MailboxMutationRecordId recordId,
            MailboxMutationStage stage,
            RemoteEmailPlacement? placement,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordFailureAsync(
            IPersistenceSession session,
            MailboxMutationRecordId recordId,
            MailFathomErrorCode failure,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
            MailAccountId accountId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MailboxMutationLifecycleCount>> ReadLifecycleCountsAsync(
            MailAccountId accountId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>A session that commits, which is what a call writing several records has to be given.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
