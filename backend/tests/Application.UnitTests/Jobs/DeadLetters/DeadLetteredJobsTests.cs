// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.DeadLetters;

/// <summary>Covers what an operator may do about background work that stopped, which is not one decision but two.</summary>
/// <remarks>
/// Watching a queue and acting on it are published under different grants, so the tests here are about which grant each
/// operation asks for and about the store never being reached without it. What the store then does with a retry or a
/// drop is the store's own contract and is covered where the store is.
/// </remarks>
public sealed class DeadLetteredJobsTests
{
    private static readonly JobId Job = JobId.Create(Guid.CreateVersion7(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero)));

    private readonly IDeadLetteredJobStore store = Substitute.For<IDeadLetteredJobStore>();

    [Fact]
    public async Task ReadPageAsync_ACallerGrantedTheAdministrativeRead_IsServedThePageTheStoreHolds()
    {
        // Arrange
        var page = new DeadLetteredJobPage([], NextCursor: null);
        this.store.ReadPageAsync(Arg.Any<DeadLetteredJobQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var deadLetters = this.JobsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var read = await deadLetters.ReadPageAsync(EveryDeadLetter(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(page, read);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var deadLetters = this.JobsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deadLetters.ReadPageAsync(EveryDeadLetter(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
        await this.store.DidNotReceive().ReadPageAsync(Arg.Any<DeadLetteredJobQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Returning a job to the queue makes it run against somebody's mailbox again, so watching the queue grants nothing towards it.</summary>
    [Fact]
    public async Task RetryAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var deadLetters = this.JobsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deadLetters.RetryAsync(Job, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        await this.store.DidNotReceive().RetryAsync(Arg.Any<JobId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Dropping records a decision rather than removing anything, so it asks for the operating grant and not the erasing one.</summary>
    [Fact]
    public async Task DropAsync_ACallerGrantedOnlyTheErasure_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var deadLetters = this.JobsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminErase));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deadLetters.DropAsync(Job, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        await this.store.DidNotReceive().DropAsync(Arg.Any<JobId>(), Arg.Any<CancellationToken>());
    }

    private static DeadLetteredJobQuery EveryDeadLetter() =>
        DeadLetteredJobQuery.Create(jobType: null, account: null, pageSize: null, cursor: null).Query!;

    private DeadLetteredJobs JobsFor(AccessAuthorization authorization) => new(this.store, authorization);
}
