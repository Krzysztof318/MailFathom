// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers what a caller is told about the changes it authored, and what it is told about everybody else's.</summary>
public sealed class MailboxChangeProgressReaderTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private readonly InMemoryMailboxMutationRecordStore records = new();

    /// <summary>What a caller comes back for is where its change stands, which the authoring call could not have told it.</summary>
    [Fact]
    public async Task ReadAsync_ARecordThisCallerAuthored_ReportsWhereItStands()
    {
        // Arrange
        var request = FlagRequestIn(Inbox, uid: 7);
        var opened = await this.OpenAsync(request);
        var reader = this.Reader();

        // Act
        var progress = await reader.ReadAsync([opened.Id], TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(progress);

        Assert.Equal(opened.Id, entry.RecordId);
        Assert.Equal(request.StoredEmailId, entry.StoredEmailId);
        Assert.Equal(MailboxMutation.SetSeen, entry.Mutation);
        Assert.Equal(MailboxMutationLifecycle.Pending, entry.Lifecycle);
        Assert.False(entry.IsOutcomeUnknown);
        Assert.Equal(0, entry.AttemptCount);
        Assert.Null(entry.LastFailure);
    }

    /// <summary>
    /// A change against an account nothing can reach is a state a person is shown rather than an absence they infer, so
    /// the attempts made and the failure they met are part of the answer.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AChangeAgainstAnUnreachableAccount_ReportsTheAttemptsAndTheFailure()
    {
        // Arrange
        var request = FlagRequestIn(Inbox, uid: 7);
        var opened = await this.OpenAsync(request);
        this.records.Arrange(request, record => record with
        {
            AttemptCount = 3,
            LastFailure = MailFathomErrorCode.MailboxUnavailable,
        });
        var reader = this.Reader();

        // Act
        var progress = await reader.ReadAsync([opened.Id], TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(progress);

        Assert.Equal(MailboxMutationLifecycle.Pending, entry.Lifecycle);
        Assert.Equal(3, entry.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, entry.LastFailure);
    }

    /// <summary>
    /// A move whose placement was issued and never answered for is the one outcome a caller has to be told about, since
    /// the message may be in either folder until the account's next pass re-establishes which.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AMoveWhoseOutcomeIsUnknown_SaysSoRatherThanReportingItAsPending()
    {
        // Arrange
        var request = RelocateRequestIn(Inbox, uid: 9);
        var opened = await this.OpenAsync(request);
        this.records.Arrange(request, record => record with { Stage = MailboxMutationStage.PlacementIssued });
        var reader = this.Reader();

        // Act
        var progress = await reader.ReadAsync([opened.Id], TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(progress);

        Assert.Equal(MailboxMutation.Relocate, entry.Mutation);
        Assert.True(entry.IsOutcomeUnknown);
    }

    /// <summary>A record recorded in a folder the caller may no longer read is absent, the same answer a read of that folder's mail gives.</summary>
    [Fact]
    public async Task ReadAsync_ARecordInAFolderWithheldFromTools_IsAbsentFromTheAnswer()
    {
        // Arrange
        var withheld = await this.OpenAsync(FlagRequestIn(Withheld, uid: 11));
        var readable = await this.OpenAsync(FlagRequestIn(Inbox, uid: 12));
        var reader = this.Reader();

        // Act
        var progress = await reader.ReadAsync(
            [withheld.Id, readable.Id],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(readable.Id, Assert.Single(progress).RecordId);
    }

    /// <summary>An identity this caller holds no record for is absent rather than reported, so asking cannot tell it whether somebody else's record exists.</summary>
    [Fact]
    public async Task ReadAsync_AnIdentityThisCallerHoldsNoRecordFor_IsAbsentRatherThanReported()
    {
        // Arrange
        var reader = this.Reader();

        // Act
        var progress = await reader.ReadAsync(
            [MailboxMutationRecordId.Create(Guid.CreateVersion7())],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(progress);
    }

    /// <summary>The caller supplies the identities, so without a ceiling the size of the answer would be the caller's to choose.</summary>
    [Fact]
    public async Task ReadAsync_MoreRecordsThanOneReadMayAskAbout_IsRefused()
    {
        // Arrange
        var reader = this.Reader();
        var asked = Enumerable
            .Range(0, MailboxChangeProgressReader.MaximumRecordsPerRead + 1)
            .Select(_ => MailboxMutationRecordId.Create(Guid.CreateVersion7()))
            .ToArray();

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.ReadAsync(asked, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("recordIds.Count", thrown.ParamName);
    }

    /// <summary>Reading about one's own work is the same authority as reading the mail the work is about, and a caller without it is refused.</summary>
    [Fact]
    public async Task ReadAsync_ACallerWithoutTheReadingGrant_IsRefused()
    {
        // Arrange
        var opened = await this.OpenAsync(FlagRequestIn(Inbox, uid: 7));
        var reader = this.Reader(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync([opened.Id], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    private static MailboxMutationRequest FlagRequestIn(MailFolderAlias folderAlias, uint uid) =>
        MailboxMutationRequest.SetSeen(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Account.Owner,
            OccurrenceIn(folderAlias, uid),
            Requester,
            isSeen: true);

    private static MailboxMutationRequest RelocateRequestIn(MailFolderAlias folderAlias, uint uid) =>
        MailboxMutationRequest.Relocate(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Account.Owner,
            OccurrenceIn(folderAlias, uid),
            Requester,
            RemoteFolderPath.Create("Archive"));

    private static EmailOccurrenceId OccurrenceIn(MailFolderAlias folderAlias, uint uid) => EmailOccurrenceId.Create(
        Account.Id,
        MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value)).Id,
        ImapUidValidity.Create(42),
        ImapUid.Create(uid));

    /// <summary>Writes one record down, as an authoring use case would have, so a read has something to answer about.</summary>
    /// <remarks>The session is a substitute because the in-memory store accepts one and uses none: what a session guarantees is a transaction, and there is none here.</remarks>
    private Task<MailboxMutationRecord> OpenAsync(MailboxMutationRequest request) => this.records.OpenAsync(
        Substitute.For<IPersistenceSession>(),
        request,
        TestContext.Current.CancellationToken);

    private MailboxChangeProgressReader Reader(AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        return new MailboxChangeProgressReader(
            callerAuthorization,
            new MailboxScopeResolver(
                OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account.Id)),
                StubMailFolderParticipation
                    .Mapping(new MailFolderIdentity(Account.Id, Inbox))
                    .Hiding(new MailFolderIdentity(Account.Id, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            this.records);
    }
}
