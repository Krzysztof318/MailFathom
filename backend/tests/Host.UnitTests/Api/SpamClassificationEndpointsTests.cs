// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.History;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the terms a run is started on, what bounds them, and what a reading of the records may carry.</summary>
/// <remarks>
/// The scope is the subject of most of it: the configured scope is both the default and the bound, because the
/// classifier declines an occurrence outside it message by message, so a run over a folder nobody configured would read
/// a whole folder and record nothing. The other half is the dry run being what a caller gets by not asking for anything.
/// </remarks>
public sealed class SpamClassificationEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>The account a stored run names, which is the owner and the identifier together.</summary>
    private static readonly MailAccountIdentity AccountIdentity =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Account);

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private readonly ISpamClassificationRunStore runs = Substitute.For<ISpamClassificationRunStore>();

    private readonly ISpamClassificationHistoryReader classifications =
        Substitute.For<ISpamClassificationHistoryReader>();

    private readonly FakeTimeProvider timeProvider = new(Now);

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while every spam command reaches a 404 that
    /// reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void Routes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/spam/runs", SpamClassificationEndpoints.RunsRoute);
        Assert.Equal("/spam/classifications", SpamClassificationEndpoints.ClassificationsRoute);
    }

    [Fact]
    public void MapSpamClassification_TheRunRoute_CarriesTheRequestBodyBound()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(string.Empty).MapSpamClassification();

        // Assert
        var runRoute = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(endpoint => endpoint.RoutePattern.RawText == SpamClassificationEndpoints.RunsRoute
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        Assert.Equal(
            SpamClassificationEndpoints.MaxRunRequestBytes,
            runRoute.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>Acting on somebody's mailbox is said rather than defaulted to, and the scope defaults to what is configured.</summary>
    [Fact]
    public async Task StartRunAsync_ARequestNamingOnlyTheAccount_StartsADryRunOverTheConfiguredScope()
    {
        // Arrange
        this.runs.FindOutstandingAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((SpamClassificationRun?)null);

        // Act
        var result = await this.StartRunAsync(new SpamClassificationRunRequestBody(
            Account.Value,
            Folders: null,
            Apply: null,
            Rescore: null));

        // Assert
        var started = Assert.IsType<Ok<SpamClassificationRunStartResponse>>(result.Result);
        Assert.True(started.Value!.Started);
        Assert.Equal(nameof(SpamActionPosture.DryRun), started.Value.Run.Posture);
        Assert.Equal([Inbox.Value], started.Value.Run.Folders);
        Assert.False(started.Value.Run.Rescores);
        Assert.Null(started.Value.Run.Profile);
    }

    [Fact]
    public async Task StartRunAsync_ARequestAskingToApply_StartsARunThatActs()
    {
        // Arrange
        this.runs.FindOutstandingAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((SpamClassificationRun?)null);

        // Act
        var result = await this.StartRunAsync(new SpamClassificationRunRequestBody(
            Account.Value,
            [Inbox.Value],
            Apply: true,
            Rescore: true));

        // Assert
        var started = Assert.IsType<Ok<SpamClassificationRunStartResponse>>(result.Result);
        Assert.Equal(nameof(SpamActionPosture.Acting), started.Value!.Run.Posture);
        Assert.True(started.Value.Run.Rescores);
    }

    /// <summary>Asking twice is asking once, and the terms of the second request are not applied to the walk under way.</summary>
    [Fact]
    public async Task StartRunAsync_ARunAlreadyOutstanding_AnswersWithItAndItsOwnTerms()
    {
        // Arrange
        this.runs.FindOutstandingAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns(new SpamClassificationRun
        {
            Account = AccountIdentity,
            RequestedAt = Now.AddMinutes(-5),
            Terms = SpamClassificationRunTerms.Create([Inbox], SpamActionPosture.DryRun, rescores: false),
            ClassifiedEmailCount = 120,
        });

        // Act
        var result = await this.StartRunAsync(new SpamClassificationRunRequestBody(
            Account.Value,
            Folders: null,
            Apply: true,
            Rescore: null));

        // Assert
        var started = Assert.IsType<Ok<SpamClassificationRunStartResponse>>(result.Result);
        Assert.False(started.Value!.Started);
        Assert.Equal(nameof(SpamActionPosture.DryRun), started.Value.Run.Posture);
        Assert.Equal(120, started.Value.Run.ClassifiedEmailCount);
        await this.runs.DidNotReceive().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<SpamClassificationRun>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A run over a folder nobody classifies would read the whole of it and record nothing.</summary>
    [Fact]
    public async Task StartRunAsync_AFolderOutsideTheConfiguredScope_IsRefusedAndNamesTheRemedy()
    {
        // Act
        var result = await this.StartRunAsync(new SpamClassificationRunRequestBody(
            Account.Value,
            [Archive.Value],
            Apply: null,
            Rescore: null));

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.Contains(Archive.Value, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        await this.runs.DidNotReceive().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<SpamClassificationRun>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartRunAsync_AFolderNameNoAliasCouldBe_IsRefusedRatherThanRaised()
    {
        // Act
        var result = await this.StartRunAsync(new SpamClassificationRunRequestBody(
            Account.Value,
            ["in\u0007box"],
            Apply: null,
            Rescore: null));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task StartRunAsync_ARequestNamingOnlyBlankFolders_IsRefused()
    {
        // Act
        var result = await this.StartRunAsync(new SpamClassificationRunRequestBody(
            Account.Value,
            ["   "],
            Apply: null,
            Rescore: null));

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>A deployment that classifies nothing has no scope to default to, so a run over it is refused.</summary>
    [Fact]
    public async Task StartRunAsync_ADeploymentThatClassifiesNoFolder_IsRefused()
    {
        // Act
        var result = await this.StartRunAsync(
            new SpamClassificationRunRequestBody(Account.Value, Folders: null, Apply: null, Rescore: null),
            SpamClassificationSettings.Disabled);

        // Assert
        AssertRefused(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nowhere")]
    public async Task StartRunAsync_ARequestNamingNoAccountThisDeploymentServes_IsRefused(string? account)
    {
        // Act
        var result = await this.StartRunAsync(
            new SpamClassificationRunRequestBody(account, Folders: null, Apply: null, Rescore: null));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task StartRunAsync_ARequestWithNoBody_IsRefusedWithoutStartingAnything()
    {
        // Act
        var result = await this.StartRunAsync(request: null);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>An account nobody has asked for a run is an outcome this deployment can state, not a missing resource.</summary>
    [Fact]
    public async Task ReadRunAsync_AnAccountNobodyHasAskedForARun_AnswersWithNoRunRatherThanARefusal()
    {
        // Arrange
        this.runs.FindLatestAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((SpamClassificationRun?)null);

        // Act
        var result = await SpamClassificationEndpoints.ReadRunAsync(
            Account.Value,
            CatalogServing(Account),
            new SpamClassificationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        var state = Assert.IsType<Ok<SpamClassificationRunStateResponse>>(result.Result);
        Assert.Equal(Account.Value, state.Value!.Account);
        Assert.Null(state.Value.Run);
    }

    [Fact]
    public async Task ReadRunAsync_ARunThatHasEnded_ReportsItsCountsAndHowItEnded()
    {
        // Arrange
        this.runs.FindLatestAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns(new SpamClassificationRun
        {
            Account = AccountIdentity,
            RequestedAt = Now.AddHours(-1),
            Terms = SpamClassificationRunTerms.Create([Inbox], SpamActionPosture.Acting, rescores: false),
            Profile = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5),
            ClassifiedEmailCount = 400,
            SpamEmailCount = 12,
            SkippedEmailCount = 3,
            ActedEmailCount = 12,
            EndedAt = Now.AddMinutes(-50),
            Ending = SpamClassificationRunEnding.Completed,
        });

        // Act
        var result = await SpamClassificationEndpoints.ReadRunAsync(
            Account.Value,
            CatalogServing(Account),
            new SpamClassificationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        var run = Assert.IsType<Ok<SpamClassificationRunStateResponse>>(result.Result).Value!.Run;
        Assert.Equal((400, 12, 3, 12), (
            run!.ClassifiedEmailCount,
            run.SpamEmailCount,
            run.SkippedEmailCount,
            run.ActedEmailCount));
        Assert.Equal(nameof(SpamClassificationRunEnding.Completed), run.Ending);
        Assert.NotNull(run.Profile);
    }

    [Fact]
    public async Task ReadRunAsync_AnAccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Act
        var result = await SpamClassificationEndpoints.ReadRunAsync(
            "nowhere",
            CatalogServing(Account),
            new SpamClassificationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>The signals reach the wire by name, and the change a verdict asked for is pointed at rather than described.</summary>
    [Fact]
    public async Task ReadClassificationsAsync_ARecordedClassification_ServesItsSignalsByNameAndPointsAtTheMutation()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        this.ArrangePage(new SpamClassificationHistoryEntry(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Inbox,
            SpamVerdict.Spam,
            SpamClassificationStage.Scanner,
            SpamAssessment.Create(15.2, 5.0),
            "4.0.2",
            SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5),
            ["X-Spam-Flag", "BAYES_99"],
            Now,
            [new SpamClassificationRequestedMutation(recordId, MailboxMutation.Relocate)]));

        // Act
        var result = await this.ReadClassificationsAsync();

        // Assert
        var served = Assert.Single(Assert.IsType<Ok<SpamClassificationPageResponse>>(result.Result)
            .Value!
            .Classifications);
        Assert.Equal(["X-Spam-Flag", "BAYES_99"], served.Signals);
        Assert.Equal(
            (recordId.Value, MailboxMutation.Relocate.Name),
            (served.RequestedMutations[0].Record, served.RequestedMutations[0].Mutation));
    }

    [Fact]
    public async Task ReadClassificationsAsync_AVerdictFilter_NarrowsTheReadingToIt()
    {
        // Arrange
        this.ArrangePage();

        // Act
        await this.ReadClassificationsAsync(verdict: "spam");

        // Assert
        await this.classifications.Received(1).ReadPageAsync(
            Arg.Is<SpamClassificationHistoryQuery>(query => query!.Verdict == SpamVerdict.Spam),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadClassificationsAsync_AVerdictNoRecordCouldCarry_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(verdict: "junk");

        // Assert
        AssertRefused(result.Result);
        await this.classifications.DidNotReceive().ReadPageAsync(
            Arg.Any<SpamClassificationHistoryQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadClassificationsAsync_ACursorThisDeploymentDidNotIssue_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(cursor: "not-a-cursor!!");

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadClassificationsAsync_APageSizeOutsideTheServedRange_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(pageSize: SpamClassificationHistoryQuery.MaximumPageSize + 1);

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadClassificationsAsync_AnAccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(account: "nowhere");

        // Assert
        AssertRefused(result.Result);
    }

    private static ProblemHttpResult AssertRefused(IResult result)
    {
        var refusal = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        return refusal;
    }

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. accounts.Select(account => new ServedMailAccount(
                SyntheticMailOwner.Deployment,
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ]);

        return catalog;
    }

    private static ISpamClassificationSettingsReader SettingsReader(SpamClassificationSettings settings)
    {
        var reader = Substitute.For<ISpamClassificationSettingsReader>();
        reader.Settings.Returns(settings);

        return reader;
    }

    private void ArrangePage(params SpamClassificationHistoryEntry[] entries) =>
        this.classifications
            .ReadPageAsync(Arg.Any<SpamClassificationHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SpamClassificationHistoryPage(entries, NextCursor: null));

    private Task<Results<Ok<SpamClassificationRunStartResponse>, ProblemHttpResult>> StartRunAsync(
        SpamClassificationRunRequestBody? request,
        SpamClassificationSettings? settings = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return SpamClassificationEndpoints.StartRunAsync(
            request,
            CatalogServing(Account),
            SettingsReader(settings ?? SpamClassificationSettings.Create(
                isEnabled: true,
                usesScanner: false,
                [Inbox])),
            new SpamClassificationRunRequests(
                this.runs,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions(),
                    this.timeProvider),
                this.timeProvider,
                AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);
    }

    private Task<Results<Ok<SpamClassificationPageResponse>, ProblemHttpResult>> ReadClassificationsAsync(
        string? account = null,
        Guid? email = null,
        string? verdict = null,
        DateTimeOffset? from = null,
        DateTimeOffset? before = null,
        int? pageSize = null,
        string? cursor = null) =>
        SpamClassificationEndpoints.ReadClassificationsAsync(
            account ?? Account.Value,
            email,
            verdict,
            from,
            before,
            pageSize,
            cursor,
            CatalogServing(Account),
            new SpamClassificationHistory(this.classifications, AdministrativeGrant.WholeSurface),
            TestContext.Current.CancellationToken);

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
