// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Actions;

/// <summary>Covers what a matched rule writes down, what identity it writes it under, and what it refuses to write.</summary>
public sealed class MailRuleActionRecorderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());
    private static readonly MailRuleSetRevision Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6");

    private readonly InMemoryMailboxMutationRecordStore records = new();
    private readonly InMemoryMailFolderResolutionStore folders = new();
    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    private readonly IMailRuleActionPermissionReader permissions =
        Substitute.For<IMailRuleActionPermissionReader>();

    public MailRuleActionRecorderTests()
    {
        this.dispositions
            .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
            .Returns(AuthoredDeleteEmailDisposition.RetainTombstone);

        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });
    }

    [Fact]
    public async Task RecordAsync_ARuleFilingMail_WritesARelocationNamingTheBoundFolder()
    {
        // Arrange
        var binding = this.folders.Bind(Account, Archive, "INBOX/Archive");

        // Act
        var recording = await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(Archive)));

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
        this.folders.Bind(Account, Archive);

        // Act
        await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(Archive)));

        // Assert
        Assert.Null(Assert.Single(this.records.OpenedRequests).LocalDisposition);
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
        // Act
        var recording = await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(Archive)));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal("file-invoices", failure.RuleName);
        Assert.Equal(MailRuleActionFailureReason.DestinationFolderUnresolved, failure.Reason);
        Assert.Equal(Archive, failure.DestinationAlias);
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

    /// <summary>Revoking one action leaves the others alone, which is what makes the four switches four decisions.</summary>
    [Fact]
    public async Task RecordAsync_AFlagBesideARevokedRelocation_StillWritesTheFlag()
    {
        // Arrange
        this.folders.Bind(Account, Archive);
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default with { PermitsRelocate = false });
        var plan = MailRuleActionPlan.Compose(
        [
            RuleNamed("file-invoices", MailRuleAction.Relocate(Archive)),
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
        this.folders.Bind(Account, Archive);
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(_ => throw new InvalidOperationException("Account 'work' is not configured."));

        // Act
        var recording = await this.RecordAsync(Planned("file-invoices", MailRuleAction.Relocate(Archive)));

        // Assert
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(0, recording.RecordedCount);
        var failure = Assert.Single(recording.Failures);
        Assert.Equal(MailRuleActionFailureReason.AccountNoLongerConfigured, failure.Reason);
        Assert.Equal(Archive, failure.DestinationAlias);
    }

    /// <summary>One failing action must not cost the ones beside it, which is why each is recorded on its own.</summary>
    [Fact]
    public async Task RecordAsync_AFlagBesideAnUnresolvableDestination_StillWritesTheFlag()
    {
        // Arrange
        var plan = MailRuleActionPlan.Compose(
        [
            RuleNamed("file-invoices", MailRuleAction.Relocate(Archive)),
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
        this.folders.Bind(Account, Archive);
        var recorder = this.CreateRecorder();
        var plan = MailRuleActionPlan.Compose([RuleNamed("file-invoices", MailRuleAction.Relocate(Archive))]);

        // Act
        foreach (var uid in Enumerable.Range(1, 3))
        {
            await recorder.RecordAsync(
                Substitute.For<IPersistenceSession>(),
                StoredEmailId.Create(Guid.CreateVersion7()),
                OccurrenceAt((uint)uid),
                plan,
                Revision,
                TestContext.Current.CancellationToken);
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
        Account,
        new MailFolderResolutionId(Inbox, MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(42),
        ImapUid.Create(uid));

    private Task<MailRuleActionRecording> RecordAsync(MailRuleActionPlan plan, MailRuleSetRevision? revision = null) =>
        this.CreateRecorder().RecordAsync(
            Substitute.For<IPersistenceSession>(),
            LocalEmail,
            OccurrenceAt(7),
            plan,
            revision ?? Revision,
            TestContext.Current.CancellationToken);

    private MailRuleActionRecorder CreateRecorder() =>
        new(this.records, this.folders, this.dispositions, this.permissions);
}
