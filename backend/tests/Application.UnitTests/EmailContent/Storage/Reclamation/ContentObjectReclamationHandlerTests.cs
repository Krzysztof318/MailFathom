// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage.Reclamation;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage.Reclamation;

/// <summary>Covers what one segment of a sweep runs, and what it hands to the segment after it.</summary>
/// <remarks>
/// The claim worth proving is the one that keeps a bucket larger than one attempt reachable: a segment that stopped
/// part-way enqueues the rest under a key no other segment of any sweep shares. Without it every occasion would list
/// the same first pages and the tail would never be swept.
/// </remarks>
public sealed class ContentObjectReclamationHandlerTests
{
    /// <summary>A sweep that reached the end of the listing is finished, so nothing is owed to the queue.</summary>
    [Fact]
    public async Task RunAsync_ARunThatReachedTheEndOfTheListing_EnqueuesNoFurtherSegment()
    {
        // Arrange
        var jobs = Substitute.For<IJobStore>();
        var reclamation = Substitute.For<IContentObjectReclamation>();
        reclamation.ReclaimAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ContentObjectReclamationRun { ReclaimedCount = 3 }));

        var handler = new ContentObjectReclamationHandler(jobs, reclamation);

        // Act
        await handler.RunAsync(
            ReclaimContentObjectsJobPayload.FromTheStart(),
            TestContext.Current.CancellationToken);

        // Assert
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A segment that stopped part-way hands the rest on, carrying the position the next one resumes from.</summary>
    [Fact]
    public async Task RunAsync_ARunThatStoppedPartWay_EnqueuesTheSegmentThatCarriesTheRest()
    {
        // Arrange
        var jobs = JobStoreAccepting();
        var reclamation = Substitute.For<IContentObjectReclamation>();
        reclamation.ReclaimAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ContentObjectReclamationRun { ResumeFrom = "next-page" }));

        var handler = new ContentObjectReclamationHandler(jobs, reclamation);

        // Act
        await handler.RunAsync(
            ReclaimContentObjectsJobPayload.FromTheStart(),
            TestContext.Current.CancellationToken);

        // Assert
        await jobs.Received(1).EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request =>
                request != null
                && request.JobType == JobType.ReclaimContentObjects
                && request.AccountId == null
                && ((ReclaimContentObjectsJobPayload)request.Payload).ResumeFrom == "next-page"
                && ((ReclaimContentObjectsJobPayload)request.Payload).Segment == 1),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A run begins where the segment it is carrying says it begins, which is what makes a chain a sweep.</summary>
    [Fact]
    public async Task RunAsync_ASegmentCarryingAPosition_ResumesTheSweepFromIt()
    {
        // Arrange
        var jobs = JobStoreAccepting();
        var reclamation = Substitute.For<IContentObjectReclamation>();
        reclamation.ReclaimAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ContentObjectReclamationRun.None));

        var handler = new ContentObjectReclamationHandler(jobs, reclamation);

        // Act
        await handler.RunAsync(
            ReclaimContentObjectsJobPayload.FromTheStart().ContinuingFrom("half-way"),
            TestContext.Current.CancellationToken);

        // Assert
        await reclamation.Received(1).ReclaimAsync("half-way", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A deployment that stores content in the database has no bucket, and a segment enqueued before an endpoint was
    /// taken away still has to be answered rather than left for a handler nothing registered.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADeploymentWithNoEndpoint_ReclaimsNothingAndHandsOnNothing()
    {
        // Arrange
        var jobs = Substitute.For<IJobStore>();
        var handler = new ContentObjectReclamationHandler(jobs);

        // Act
        await handler.RunAsync(
            ReclaimContentObjectsJobPayload.FromTheStart(),
            TestContext.Current.CancellationToken);

        // Assert
        await jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A type names exactly one payload contract, so a document of another shape is a defect rather than work.</summary>
    [Fact]
    public async Task RunAsync_APayloadOfAnotherContract_IsRefused()
    {
        // Arrange
        var handler = new ContentObjectReclamationHandler(
            Substitute.For<IJobStore>(),
            Substitute.For<IContentObjectReclamation>());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.RunAsync(
                RederiveStoredMailJobPayload.For(
                    Domain.Accounts.MailAccountId.Create("work"),
                    folderAlias: null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Construction_WithoutTheQueue_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => new ContentObjectReclamationHandler(null!));

    private static IJobStore JobStoreAccepting()
    {
        var jobs = Substitute.For<IJobStore>();
        jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7()))));

        return jobs;
    }
}
