// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobEnqueueRequestTests
{
    private static ClassifyEmailSpamJobPayload Payload => ClassifyEmailSpamJobPayload.For(EmailOccurrenceId.Create(
        MailAccountId.Create("account-a"),
        new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(12345),
        ImapUid.Create(4711)));

    /// <summary>The type is read from the payload rather than supplied beside it, so the two can never disagree.</summary>
    [Fact]
    public void JobType_OfARequest_IsTheOneItsPayloadIsTheContractOf()
    {
        // Act
        var request = JobEnqueueRequest.Create(
            JobIdempotencyKey.Create("account-a/inbox#1/12345/4711"),
            Payload,
            MailAccountId.Create("account-a"));

        // Assert
        Assert.Equal(JobType.ClassifyEmailSpam, request.JobType);
    }

    /// <summary>Work with no available instant is claimable at once, which is what an arrival trigger asks for.</summary>
    [Fact]
    public void Create_WithoutAnAvailableInstant_LeavesTheJobClaimableImmediately()
    {
        // Act
        var request = JobEnqueueRequest.Create(
            JobIdempotencyKey.Create("account-a/inbox#1/12345/4711"),
            Payload,
            MailAccountId.Create("account-a"));

        // Assert
        Assert.Null(request.AvailableAt);
    }

    [Fact]
    public void CreateAvailableAt_AnInstant_KeepsItAsWhenTheJobBecomesClaimable()
    {
        // Arrange
        var availableAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

        // Act
        var request = JobEnqueueRequest.CreateAvailableAt(
            JobIdempotencyKey.Create("account-a/inbox#1/12345/4711"),
            Payload,
            MailAccountId.Create("account-a"),
            availableAt);

        // Assert
        Assert.Equal(availableAt, request.AvailableAt);
    }

    /// <summary>A job that belongs to no account is an ordinary case, so the column it lands in is nullable.</summary>
    [Fact]
    public void Create_WithNoAccount_IsAccepted()
    {
        // Act
        var request = JobEnqueueRequest.Create(JobIdempotencyKey.Create("housekeeping"), Payload, accountId: null);

        // Assert
        Assert.Null(request.AccountId);
    }

    [Fact]
    public void Create_WithNoKeyOrNoPayload_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => JobEnqueueRequest.Create(null!, Payload, accountId: null));
        Assert.Throws<ArgumentNullException>(
            () => JobEnqueueRequest.Create(JobIdempotencyKey.Create("k"), null!, accountId: null));
    }

    /// <summary>A payload naming the unspecified default would be stored under a type name nothing parses back.</summary>
    [Fact]
    public void Create_APayloadNamingNoDeclaredType_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobEnqueueRequest.Create(
            JobIdempotencyKey.Create("k"),
            new UnspecifiedJobPayload(),
            accountId: null));
    }

    private sealed record UnspecifiedJobPayload : IJobPayload
    {
        public JobType JobType => default;
    }
}
