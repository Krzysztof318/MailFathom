// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Actions;

/// <summary>Covers what a matched rule writes down, what identity it writes it under, and what it refuses to write.</summary>
public sealed class MailRuleActionRecorderTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("junk");
    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());
    private static readonly MailRuleSetRevision Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6");

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryMailboxMutationRecordStore records = new();
    private readonly InMemoryMailFolderResolutionStore folders = new();
    private readonly StubMailFolderMappings folderMappings = StubMailFolderMappings.Nothing;
    private readonly List<RemoteFolder> advertisedFolders = [];
    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    private readonly IMailRuleActionPermissionReader permissions =
        Substitute.For<IMailRuleActionPermissionReader>();

    private readonly MailboxDestinationResolver destinations;

    public MailRuleActionRecorderTests()
    {
        this.dispositions
            .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
            .Returns(AuthoredDeleteEmailDisposition.RetainTombstone);

        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });

        this.destinations = this.CreateDestinationResolver();
    }

    [Fact]
    public async Task RecordAsync_ARuleFilingMail_WritesARelocationNamingTheBoundFolder()
    {
        // Arrange
        this.MapMirrored(Archive, "INBOX/Archive");
        var binding = this.folders.Bind(Account.Id, Archive, "INBOX/Archive");

        // Act
        var recording = await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))));

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal(binding.RemotePath, request.DestinationPath);
        Assert.Empty(recording.Failures);
        Assert.Equal(1, recording.RecordedCount);
    }

    /// <summary>A relocation between mirrored folders disposes of nothing locally; the row follows the message.</summary>
    [Fact]
    public async Task RecordAsync_ARuleFilingMail_LeavesTheLocalCopyWhereTheDestinationWillCarryIt()
    {
        // Arrange
        this.MapMirrored(Archive, "INBOX/Archive");
        this.folders.Bind(Account.Id, Archive);

        // Act
        await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))));

        // Assert
        Assert.Null(Assert.Single(this.records.OpenedRequests).LocalDisposition);
    }

    /// <summary>A message moved somewhere MailFathom keeps no copy of has left the mirrored mailbox exactly as a delete does.</summary>
    [Fact]
    public async Task RecordAsync_ARuleFilingIntoAFolderNothingMirrors_CarriesTheAccountsLocalDisposition()
    {
        // Arrange
        this.MapUnmirrored(Junk, "INBOX.Spam");

        // Act
        var recording = await this.RecordAsync(Planned("file-spam", MailRuleAction.Relocate(MailFolderReference.ToAlias(Junk))));

        // Assert
        Assert.Empty(recording.Failures);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal("INBOX.Spam", request.DestinationPath!.Value.Value);
        Assert.Equal(AuthoredDeleteEmailDisposition.RetainTombstone, request.LocalDisposition);
    }

    /// <summary>A copy leaves the source where it is, so nothing local is disposed of whatever the destination is.</summary>
    [Fact]
    public async Task RecordAsync_ARuleCopyingIntoAFolderNothingMirrors_LeavesTheLocalStoreAlone()
    {
        // Arrange
        this.MapUnmirrored(Junk, "INBOX.Spam");

        // Act
        var recording = await this.RecordAsync(Planned("keep-a-copy", MailRuleAction.Copy(MailFolderReference.ToAlias(Junk))));

        // Assert
        Assert.Empty(recording.Failures);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Copy, request.Mutation);
        Assert.Null(request.LocalDisposition);
    }

    /// <summary>A mapping whose folder the server does not have is a refusal of its own, not a path to fall back to.</summary>
    [Fact]
    public async Task RecordAsync_AnUnmirroredDestinationTheServerDoesNotAdvertise_WritesNothingAndSaysWhy()
    {
        // Arrange
        this.folderMappings.With(
            Account.Id,
            MailFolderMapping.ToRemotePath(
                Junk,
                RemoteFolderPath.Create("INBOX.Spam"),
                MailFolderParticipation.MappedOnly));

        // Act
        var recording = await this.RecordAsync(Planned("file-spam", MailRuleAction.Relocate(MailFolderReference.ToAlias(Junk))));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(
            MailRuleActionFailureReason.DestinationFolderNotAdvertised,
            Assert.Single(recording.Failures).Reason);
    }

    [Fact]
    public async Task RecordAsync_ARuleMarkingMailRead_WritesTheFlagDirectionItDeclared()
    {
        // Act
        await this.RecordAsync(Planned("mark-them-read", MailRuleAction.SetSeen(isSeen: true)));

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.SetSeen, request.Mutation);
        Assert.True(request.DesiredSeenState);
    }

    /// <summary>Flagging is a change of its own, so it is written down as its own mutation with its own direction.</summary>
    [Fact]
    public async Task RecordAsync_ARuleFlaggingMail_WritesTheFlaggedDirectionItDeclared()
    {
        // Act
        await this.RecordAsync(Planned("flag-invoices", MailRuleAction.SetFlagged(isFlagged: true)));

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.SetFlagged, request.Mutation);
        Assert.True(request.DesiredFlaggedState);
        Assert.Null(request.DesiredSeenState);
    }

    /// <summary>A keyword names a label rather than a folder, so nothing about an account has to resolve for one to be
    /// written, and the plan's fixed order decides which request is opened first rather than the order the rules were
    /// declared in — the removal goes first so a keyword one rule adds survives another rule's removal.</summary>
    [Fact]
    public async Task RecordAsync_ARuleLabellingMail_WritesEachKeywordChangeAsItsOwnMutation()
    {
        // Arrange
        var planned = new[]
        {
            RuleNamed("label-invoices", MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["$Todo"]))),
            RuleNamed("unlabel-invoices", MailRuleAction.RemoveKeywords(AuthoredMailKeywords.Create(["$Done"]))),
        };

        // Act
        await this.RecordAsync(MailRuleActionPlan.Compose(planned));

        // Assert
        Assert.Equal(
            [MailboxMutation.RemoveKeywords, MailboxMutation.AddKeywords],
            this.records.OpenedRequests.Select(request => request.Mutation));
        Assert.Equal(
            [AuthoredMailKeywords.Create(["$Done"]), AuthoredMailKeywords.Create(["$Todo"])],
            this.records.OpenedRequests.Select(request => request.Keywords));
    }

    /// <summary>Naming no keyword is how a replacement clears them all, so it reaches the record as a change rather than as nothing.</summary>
    [Fact]
    public async Task RecordAsync_ARuleClearingEveryKeyword_WritesAReplacementNamingNone()
    {
        // Act
        await this.RecordAsync(Planned("clear-labels", MailRuleAction.SetKeywords(AuthoredMailKeywords.None)));

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.SetKeywords, request.Mutation);
        Assert.True(request.Keywords?.IsEmpty);
    }

    /// <summary>The disposition is the account's answer at the moment the request is written, exactly as an authored delete's is.</summary>
    [Fact]
    public async Task RecordAsync_ARuleDeletingMail_CarriesTheAccountsLocalDisposition()
    {
        // Act
        await this.RecordAsync(Planned("drop-notifications", MailRuleAction.Delete()));

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Delete, request.Mutation);
        Assert.Equal(AuthoredDeleteEmailDisposition.RetainTombstone, request.LocalDisposition);
    }

    /// <summary>The identity is the occurrence, the rule with its revision, and the mutation, so asking again asks once.</summary>
    [Fact]
    public async Task RecordAsync_TheSameRuleAndRevisionTwice_OpensOneRecord()
    {
        // Arrange
        var planned = Planned("mark-them-read", MailRuleAction.SetSeen(isSeen: true));

        // Act
        await this.RecordAsync(planned);
        var second = await this.RecordAsync(planned);

        // Assert
        Assert.Equal(1, this.records.OpenedRecordCount);
        Assert.Equal(1, second.RecordedCount);
    }

    /// <summary>An edited rule set is a different revision, so the same rule over the same email asks afresh.</summary>
    [Fact]
    public async Task RecordAsync_TheSameRuleUnderANewRevision_OpensASecondRecord()
    {
        // Arrange
        var planned = Planned("mark-them-read", MailRuleAction.SetSeen(isSeen: true));

        // Act
        await this.RecordAsync(planned);
        await this.RecordAsync(planned, MailRuleSetRevision.Restore("ffffffffffff"));

        // Assert
        Assert.Equal(2, this.records.OpenedRecordCount);
    }

    /// <summary>Filing into the nearest folder whose name looks right is precisely what a stale destination must not do.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationNothingHasBound_WritesNothingAndSaysWhy()
    {
        // Arrange
        this.MapMirrored(Archive, "INBOX/Archive");

        // Act
        var recording = await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal("file-invoices", failure.RuleName);
        Assert.Equal(MailRuleActionFailureReason.DestinationFolderUnresolved, failure.Reason);
        Assert.Equal(MailFolderReference.ToAlias(Archive), failure.Destination);
    }

    /// <summary>A rule naming what the folder is for files into whatever this account calls that folder.</summary>
    [Fact]
    public async Task RecordAsync_ARuleFilingIntoARole_WritesARelocationNamingTheFolderPlayingIt()
    {
        // Arrange
        this.folderMappings.With(
            Account.Id,
            MailFolderMapping.ToRemotePath(
                Archive,
                RemoteFolderPath.Create("INBOX/Archive"),
                specialUse: MailFolderSpecialUse.Archive));
        var binding = this.folders.Bind(Account.Id, Archive, "INBOX/Archive");

        // Act
        var recording = await this.RecordAsync(
            Planned("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToRole(MailFolderSpecialUse.Archive))));

        // Assert
        Assert.Empty(recording.Failures);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal(binding.RemotePath, request.DestinationPath);
    }

    /// <summary>A role no folder of this account carries stops the one action rather than the whole pass, so every other rule still runs.</summary>
    [Fact]
    public async Task RecordAsync_ARuleFilingIntoARoleTheAccountDoesNotMap_WritesNothingAndSaysWhy()
    {
        // Act
        var recording = await this.RecordAsync(
            Planned("file-spam", MailRuleAction.Relocate(MailFolderReference.ToRole(MailFolderSpecialUse.Junk))));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal(MailRuleActionFailureReason.DestinationFolderUnmapped, failure.Reason);
        Assert.Equal(MailFolderReference.ToRole(MailFolderSpecialUse.Junk), failure.Destination);
    }

    /// <summary>What a withdrawn account decided about its own deletions is unknown, and no value invented here would be it.</summary>
    [Fact]
    public async Task RecordAsync_AnAccountTheConfigurationNoLongerDeclares_RefusesTheDeletionVisibly()
    {
        // Arrange
        this.dispositions
            .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
            .Returns(_ => throw new InvalidOperationException("Account 'work' is not configured."));

        // Act
        var recording = await this.RecordAsync(Planned("drop-notifications", MailRuleAction.Delete()));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal(MailRuleActionFailureReason.AccountNoLongerConfigured, failure.Reason);
    }

    /// <summary>The two sections reload apart, so a revoked permission has to reach the next pass rather than the next edit of the rules.</summary>
    [Fact]
    public async Task RecordAsync_AnActionTheAccountHasStoppedPermitting_WritesNothingAndSaysWhy()
    {
        // Arrange
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = false });

        // Act
        var recording = await this.RecordAsync(Planned("drop-notifications", MailRuleAction.Delete()));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal("drop-notifications", failure.RuleName);
        Assert.Equal(MailRuleActionFailureReason.ActionNoLongerPermitted, failure.Reason);
    }

    /// <summary>
    /// The recorder judges each action against the account's permissions again as it writes, which is a separate check
    /// from the one startup ran over the rule set. A revoked switch therefore has to reach it, and the two switches this
    /// tier added are the ones a re-check narrowed back to the original actions would silently let through.
    /// </summary>
    [Theory]
    [InlineData("flag-invoices", false, true)]
    [InlineData("label-invoices", true, false)]
    [InlineData("unlabel-invoices", true, false)]
    [InlineData("relabel-invoices", true, false)]
    public async Task RecordAsync_ANewActionTheAccountHasStoppedPermitting_WritesNothingAndSaysWhy(
        string ruleName,
        bool permitsSetFlagged,
        bool permitsWriteKeywords)
    {
        // Arrange
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with
            {
                PermitsSetFlagged = permitsSetFlagged,
                PermitsWriteKeywords = permitsWriteKeywords,
            });
        var labels = AuthoredMailKeywords.Create(["$Todo"]);
        MailRuleAction action = ruleName switch
        {
            "flag-invoices" => MailRuleAction.SetFlagged(isFlagged: true),
            "label-invoices" => MailRuleAction.AddKeywords(labels),
            "unlabel-invoices" => MailRuleAction.RemoveKeywords(labels),
            _ => MailRuleAction.SetKeywords(labels),
        };

        // Act
        var recording = await this.RecordAsync(Planned(ruleName, action));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal(ruleName, failure.RuleName);
        Assert.Equal(MailRuleActionFailureReason.ActionNoLongerPermitted, failure.Reason);
    }

    /// <summary>Revoking one action leaves the others alone, which is what makes the four switches four decisions.</summary>
    [Fact]
    public async Task RecordAsync_AFlagBesideARevokedRelocation_StillWritesTheFlag()
    {
        // Arrange
        this.MapMirrored(Archive, "INBOX/Archive");
        this.folders.Bind(Account.Id, Archive);
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with { PermitsRelocate = false });
        var plan = MailRuleActionPlan.Compose(
        [
            RuleNamed("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))),
            RuleNamed("mark-them-read", MailRuleAction.SetSeen(isSeen: true)),
        ]);

        // Act
        var recording = await this.RecordAsync(plan);

        // Assert
        Assert.Equal(MailboxMutation.SetSeen, Assert.Single(this.records.OpenedRequests).Mutation);
        Assert.Equal(1, recording.RecordedCount);
        Assert.Equal(
            MailRuleActionFailureReason.ActionNoLongerPermitted,
            Assert.Single(recording.Failures).Reason);
    }

    /// <summary>An account the configuration has stopped declaring permits nothing that could be asked on its behalf.</summary>
    [Fact]
    public async Task RecordAsync_AnAccountWhosePermissionsCannotBeRead_RefusesEveryActionVisibly()
    {
        // Arrange
        this.folders.Bind(Account.Id, Archive);
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(_ => throw new InvalidOperationException("Account 'work' is not configured."));

        // Act
        var recording = await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(0, recording.RecordedCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal(MailRuleActionFailureReason.AccountNoLongerConfigured, failure.Reason);
        Assert.Equal(MailFolderReference.ToAlias(Archive), failure.Destination);
    }

    /// <summary>One failing action must not cost the ones beside it, which is why each is recorded on its own.</summary>
    [Fact]
    public async Task RecordAsync_AFlagBesideAnUnresolvableDestination_StillWritesTheFlag()
    {
        // Arrange
        var plan = MailRuleActionPlan.Compose(
        [
            RuleNamed("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))),
            RuleNamed("mark-them-read", MailRuleAction.SetSeen(isSeen: true)),
        ]);

        // Act
        var recording = await this.RecordAsync(plan);

        // Assert
        Assert.Equal(MailboxMutation.SetSeen, Assert.Single(this.records.OpenedRequests).Mutation);
        Assert.Single(recording.Failures);
        Assert.Equal(1, recording.RecordedCount);
    }

    /// <summary>A batch of mail matching one filing rule must not re-read one binding per message.</summary>
    [Fact]
    public async Task RecordAsync_TheSameDestinationOverSeveralEmails_ResolvesTheBindingOnce()
    {
        // Arrange
        this.MapMirrored(Archive, "INBOX/Archive");
        this.folders.Bind(Account.Id, Archive);
        var recorder = this.CreateRecorder();
        var plan = MailRuleActionPlan.Compose([RuleNamed("file-invoices", MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)))]);

        // Act
        foreach (var uid in Enumerable.Range(1, 3))
        {
            await this.RecordAsync(
                recorder,
                StoredEmailId.Create(Guid.CreateVersion7()),
                OccurrenceAt((uint)uid),
                plan,
                Revision);
        }

        // Assert
        Assert.Equal(1, this.folders.ResolutionReadCount);
        Assert.Equal(3, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_APlanThatAsksForNothing_WritesNothing()
    {
        // Act
        var recording = await this.RecordAsync(MailRuleActionPlan.Nothing);

        // Assert
        Assert.Same(MailRuleActionRecording.Nothing, recording);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    private static MailRuleActionPlan Planned(string ruleName, MailRuleAction action) =>
        MailRuleActionPlan.Compose([RuleNamed(ruleName, action)]);

    private static MailRule RuleNamed(string name, MailRuleAction action) => MailRule.Create(
        name,
        ScriptedMailRuleCondition.Answering(matches: true),
        MailRuleActionSet.Create([action]));

    private static EmailOccurrenceId OccurrenceAt(uint uid) => EmailOccurrenceId.Create(
        Account.Id,
        new MailFolderResolutionId(Inbox, MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(42),
        ImapUid.Create(uid));

    private Task<MailRuleActionRecording> RecordAsync(MailRuleActionPlan plan, MailRuleSetRevision? revision = null) =>
        this.RecordAsync(this.CreateRecorder(), LocalEmail, OccurrenceAt(7), plan, revision ?? Revision);

    /// <summary>Records one email's plan the way a pass does: the destinations resolved first, the records written after.</summary>
    private async Task<MailRuleActionRecording> RecordAsync(
        MailRuleActionRecorder recorder,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrence,
        MailRuleActionPlan plan,
        MailRuleSetRevision revision)
    {
        var resolved = await this.destinations.ResolveAsync(
            Account,
            [.. plan.Actions.Select(planned => planned.Action.Destination).OfType<MailFolderReference>()],
            TestContext.Current.CancellationToken);

        return await recorder.RecordAsync(
            Substitute.For<IPersistenceSession>(),
            storedEmailId,
            Account.Owner,
            occurrence,
            plan,
            revision,
            resolved,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Maps a folder the account mirrors, which is what every destination but the unmirrored one here is.</summary>
    private void MapMirrored(MailFolderAlias alias, string remotePath) =>
        this.folderMappings.With(Account.Id, MailFolderMapping.ToRemotePath(alias, RemoteFolderPath.Create(remotePath)));

    /// <summary>Maps a folder MailFathom knows by name and mirrors nothing of, and advertises it on the server.</summary>
    private void MapUnmirrored(MailFolderAlias alias, string remotePath)
    {
        this.folderMappings.With(
            Account.Id,
            MailFolderMapping.ToRemotePath(
                alias,
                RemoteFolderPath.Create(remotePath),
                MailFolderParticipation.MappedOnly));

        this.advertisedFolders.Add(new RemoteFolder(RemoteFolderPath.Create(remotePath, '.'), []));
    }

    private MailboxDestinationResolver CreateDestinationResolver()
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RemoteFolder>>([.. this.advertisedFolders]));

        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        persistenceSessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

        return new MailboxDestinationResolver(
            this.folderMappings.Resolver,
            this.folders,
            new MailFolderResolver(
                remoteFolderCatalog,
                Substitute.For<IRemoteFolderCreator>(),
                this.folders,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                persistenceSessionFactory,
                new FakeTimeProvider(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero))),
            transportSecurityPolicies);
    }

    private MailRuleActionRecorder CreateRecorder() => new(this.records, this.dispositions, this.permissions);
}
