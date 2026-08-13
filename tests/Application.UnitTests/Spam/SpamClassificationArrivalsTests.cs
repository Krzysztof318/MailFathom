// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Spam;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

/// <summary>Covers what a stored message asks of the queue, and what it deliberately asks of nothing.</summary>
public sealed class SpamClassificationArrivalsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private static readonly StoredEmailId StoredEmail =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000000a"));

    private readonly IJobStore jobs = Substitute.For<IJobStore>();

    public SpamClassificationArrivalsTests() => this.jobs
        .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
        .Returns(JobEnqueueResult.Created(JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"))));

    [Fact]
    public async Task ScheduleAsync_AMessageInAClassifiedFolder_EnqueuesOneClassificationNamingTheOccurrence()
    {
        // Arrange
        var occurrence = OccurrenceIn(Inbox, uid: 4401);

        // Act
        await this.CreateArrivals(SettingsCovering(Inbox))
            .ScheduleAsync(StoredEmail, occurrence, TestContext.Current.CancellationToken);

        // Assert
        var request = this.EnqueuedRequests().Single();

        Assert.Equal(JobType.ClassifyEmailSpam, request.JobType);
        Assert.Equal(Account, request.AccountId);
        Assert.Equal(occurrence, Assert.IsType<EmailOccurrenceJobPayload>(request.Payload).ToOccurrenceId());
        Assert.Null(request.AvailableAt);
    }

    /// <summary>The key is the message's own stored identity, which fits the bound whatever an operator named their account and folders.</summary>
    [Fact]
    public async Task ScheduleAsync_AnAccountAndFolderNamedAtTheirGreatestLength_ComposesAKeyInsideTheBound()
    {
        // Arrange
        var longestName = new string('a', 128);
        var occurrence = EmailOccurrenceId.Create(
            MailAccountId.Create(longestName),
            new MailFolderResolutionId(
                MailFolderAlias.Create(longestName),
                MailFolderResolutionGeneration.Create(int.MaxValue)),
            ImapUidValidity.Create(uint.MaxValue),
            ImapUid.Create(uint.MaxValue));

        // Act
        await this.CreateArrivals(SettingsCovering(occurrence.FolderResolutionId.Alias))
            .ScheduleAsync(StoredEmail, occurrence, TestContext.Current.CancellationToken);

        // Assert
        var key = this.EnqueuedRequests().Single().Key.Value;

        Assert.Equal("0199a0c0-0000-7000-8000-00000000000a", key);
        Assert.True(key.Length <= JobIdempotencyKey.MaximumLength);
    }

    /// <summary>Two arrivals of one message carry one identity, so the queue answers the second with the first.</summary>
    [Fact]
    public async Task ScheduleAsync_TheSameMessageTwice_AsksUnderOneIdentity()
    {
        // Arrange
        var arrivals = this.CreateArrivals(SettingsCovering(Inbox));
        var occurrence = OccurrenceIn(Inbox, uid: 4401);

        // Act
        await arrivals.ScheduleAsync(StoredEmail, occurrence, TestContext.Current.CancellationToken);
        await arrivals.ScheduleAsync(StoredEmail, occurrence, TestContext.Current.CancellationToken);

        // Assert
        var requests = this.EnqueuedRequests();

        Assert.Equal(2, requests.Count);
        Assert.Single(requests.Select(request => request.Key.Value).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScheduleAsync_ClassificationSwitchedOff_AsksTheQueueForNothing()
    {
        // Act
        await this.CreateArrivals(SpamClassificationSettings.Disabled).ScheduleAsync(
            StoredEmail,
            OccurrenceIn(Inbox, uid: 4401),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.EnqueuedRequests());
    }

    [Fact]
    public async Task ScheduleAsync_AFolderOutsideTheConfiguredScope_AsksTheQueueForNothing()
    {
        // Act
        await this.CreateArrivals(SettingsCovering(Inbox)).ScheduleAsync(
            StoredEmail,
            OccurrenceIn(Archive, uid: 4401),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.EnqueuedRequests());
    }

    /// <summary>A full queue is backpressure the arrival path absorbs: the wait a verdict is allowed releases the message instead.</summary>
    [Fact]
    public async Task ScheduleAsync_AQueueAlreadyAtItsDepthBound_ReportsNothingToTheCaller()
    {
        // Arrange
        this.jobs
            .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.RefusedAtCapacity());

        // Act
        await this.CreateArrivals(SettingsCovering(Inbox)).ScheduleAsync(
            StoredEmail,
            OccurrenceIn(Inbox, uid: 4401),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(this.EnqueuedRequests());
    }

    private static SpamClassificationSettings SettingsCovering(params MailFolderAlias[] aliases) =>
        SpamClassificationSettings.Create(isEnabled: true, usesScanner: false, aliases);

    private static EmailOccurrenceId OccurrenceIn(MailFolderAlias alias, uint uid) => EmailOccurrenceId.Create(
        Account,
        new MailFolderResolutionId(alias, MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(9),
        ImapUid.Create(uid));

    private SpamClassificationArrivals CreateArrivals(SpamClassificationSettings settings)
    {
        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.Settings.Returns(settings);

        return new SpamClassificationArrivals(this.jobs, settingsReader);
    }

    private IReadOnlyList<JobEnqueueRequest> EnqueuedRequests() =>
    [
        .. this.jobs
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IJobStore.EnqueueAsync))
            .Select(call => (JobEnqueueRequest)call.GetArguments()[0]!),
    ];
}
