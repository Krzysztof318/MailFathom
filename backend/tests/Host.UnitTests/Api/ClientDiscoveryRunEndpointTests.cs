// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the two Discover routes accept off the wire, what they refuse, and what a reader is streamed.</summary>
/// <remarks>
/// The run itself is covered where it happens. What is asserted here is the transport: which questions are refused
/// before a run is opened at all, that a run belongs to the owner who asked for it, and that a reconnecting client
/// stating where it left off is given what it missed rather than the run over again.
/// </remarks>
public sealed class ClientDiscoveryRunEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void DiscoveryRunsRoute_IsThePathAClientComposes() =>
        Assert.Equal("/discovery/runs", ClientDiscoveryRunEndpoints.DiscoveryRunsRoute);

    /// <summary>The reading path is composed by a client too, and a browser's own reconnection re-requests exactly it.</summary>
    [Fact]
    public void DiscoveryRunEventsRoute_IsThePathAClientComposes() =>
        Assert.Equal("/discovery/runs/{runId:guid}/events", ClientDiscoveryRunEndpoints.DiscoveryRunEventsRoute);

    /// <summary>A question the owner may ask opens a run and answers with where that run is read, which is all a client needs.</summary>
    [Fact]
    public void Start_AQuestionOverTheOwnersOwnMail_OpensARunAndNamesWhereItIsRead()
    {
        // Arrange
        var registry = NewRegistry();

        // Act
        var answered = Start(new ClientDiscoveryRunRequest("which supplier quoted least", null, null, null, null), registry);

        // Assert
        var accepted = Assert.IsType<Accepted<ClientDiscoveryRunResponse>>(answered.Result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(1, registry.HeldCount);
        Assert.Equal(
            $"/api/client/discovery/runs/{accepted.Value.RunId}/events",
            accepted.Location);
    }

    /// <summary>A question with no text is refused where a person can act on it rather than opening a run that fails at once.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Start_AQuestionCarryingNoText_RefusesItBeforeOpeningARun(string? question)
    {
        // Arrange
        var registry = NewRegistry();

        // Act
        var answered = Start(new ClientDiscoveryRunRequest(question, null, null, null, null), registry);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, registry.HeldCount);
    }

    /// <summary>An account this owner does not own is refused as a request to change rather than narrowed away in silence.</summary>
    [Fact]
    public void Start_AnAccountThisOwnerDoesNotOwn_RefusesTheQuestion()
    {
        // Arrange
        var registry = NewRegistry();

        // Act
        var answered = Start(
            new ClientDiscoveryRunRequest("which supplier quoted least", ["somebody-elses"], null, null, null),
            registry);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, registry.HeldCount);
    }

    /// <summary>A run executes as whoever asked for it, so a request the transport admitted nobody for opens none.</summary>
    [Fact]
    public void Start_ARequestNoPrincipalWasAdmittedFor_RefusesTheQuestion()
    {
        // Arrange
        var registry = NewRegistry();
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns((AuthorizedPrincipal?)null);

        // Act
        var answered = ClientDiscoveryRunEndpoints.Start(
            new ClientDiscoveryRunRequest("which supplier quoted least", null, null, null, null),
            ResolverFor(SyntheticMailOwner.Deployment),
            principals,
            registry,
            LauncherOver(registry));

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Equal(0, registry.HeldCount);
    }

    /// <summary>A client looping over the asking route is told to wait rather than being allowed to fill this process.</summary>
    [Fact]
    public void Start_BeyondWhatThisProcessRunsAtOnce_RefusesWithTooManyRequests()
    {
        // Arrange
        var registry = NewRegistry();
        Enumerable.Range(0, DiscoveryRunBounds.MaximumConcurrentRuns)
            .ToList()
            .ForEach(run => registry.TryOpen(SyntheticMailOwner.Deployment, out _));

        // Act
        var answered = Start(new ClientDiscoveryRunRequest("which supplier quoted least", null, null, null, null), registry);

        // Assert
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
    }

    /// <summary>An identifier alone says nothing about whether a run exists, so somebody else's reads as no such run.</summary>
    [Fact]
    public void Watch_ARunAnotherOwnerStarted_ReportsNoSuchRun()
    {
        // Arrange
        var registry = NewRegistry();
        registry.TryOpen(SyntheticMailOwner.Another, out var journal);
        Assert.NotNull(journal);

        // Act
        var answered = Watch(journal.Id.Value, registry);

        // Assert
        Assert.IsType<NotFound>(answered.Result);
    }

    /// <summary>An identifier no run could carry is answered as no such run rather than reaching the registry at all.</summary>
    [Fact]
    public void Watch_AnIdentifierNoRunCouldCarry_ReportsNoSuchRun() =>
        Assert.IsType<NotFound>(Watch(Guid.Empty, NewRegistry()).Result);

    /// <summary>Each event reaches the client under its own sequence and its own name, which is what a client renders on.</summary>
    [Fact]
    public async Task Watch_ARunThisOwnerHolds_StreamsEveryEventUnderItsSequenceAndItsName()
    {
        // Arrange
        var registry = NewRegistry();
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunStarted());
        journal.Append(new DiscoveryRunCompleted([]));

        // Act
        var streamed = await StreamedBody(journal, registry, resumedFrom: null);

        // Assert
        Assert.Contains("id: 1", streamed, StringComparison.Ordinal);
        Assert.Contains($"event: {DiscoveryRunStarted.Kind}", streamed, StringComparison.Ordinal);
        Assert.Contains("id: 2", streamed, StringComparison.Ordinal);
        Assert.Contains($"event: {DiscoveryRunCompleted.Kind}", streamed, StringComparison.Ordinal);
    }

    /// <summary>An event names its kind once and carries nothing of how this process reads the run it belongs to.</summary>
    /// <remarks>
    /// The name and the ending are read in process — one names the transport's own field, the other stops the journal —
    /// and neither is part of what a client is told, which the discriminator already carries. The serializer reads the
    /// attribute that keeps them out off the member being written rather than off the one it overrides, so this is what
    /// fails when an event is added without repeating it.
    /// </remarks>
    [Fact]
    public async Task Watch_ARunThisOwnerHolds_WritesTheKindOnceAndNothingThisProcessReadsTheRunBy()
    {
        // Arrange
        var registry = NewRegistry();
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunStarted());
        journal.Append(new DiscoveryRunCompleted([]));

        // Act
        var streamed = await StreamedBody(journal, registry, resumedFrom: null);

        // Assert
        Assert.Contains($"\"event\":\"{DiscoveryRunStarted.Kind}\"", streamed, StringComparison.Ordinal);
        Assert.DoesNotContain("eventName", streamed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endsTheRun", streamed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Why a run ended crosses the wire as the word a client branches on, which is the enum member's own name.</summary>
    /// <remarks>
    /// The value is a closed set a screen decides what to offer next from, so its spelling is the contract rather than an
    /// artefact of how it is serialized — nothing on this surface applies a naming policy to an enum, and a client
    /// matching a differently-cased word would fall through every branch it has and offer nothing.
    /// </remarks>
    [Fact]
    public async Task Watch_ARunThatEndedBadly_WritesWhyAsTheClosedValuesOwnName()
    {
        // Arrange
        var registry = NewRegistry();
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunStarted());
        journal.Append(new DiscoveryRunFailed(DiscoveryRunFailure.TimedOut));

        // Act
        var streamed = await StreamedBody(journal, registry, resumedFrom: null);

        // Assert
        Assert.Contains("\"failure\":\"TimedOut\"", streamed, StringComparison.Ordinal);
    }

    /// <summary>A dropped connection is resumed from the place the protocol's own header states, so nothing is sent twice.</summary>
    [Fact]
    public async Task Watch_AClientStatingWhereItLeftOff_StreamsOnlyWhatItMissed()
    {
        // Arrange
        var registry = NewRegistry();
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunStarted());
        journal.Append(new DiscoveryRunCompleted([PresentationLimitation.RetrievalTruncated]));

        // Act
        var streamed = await StreamedBody(journal, registry, resumedFrom: "1");

        // Assert
        Assert.DoesNotContain($"event: {DiscoveryRunStarted.Kind}", streamed, StringComparison.Ordinal);
        Assert.Contains("id: 2", streamed, StringComparison.Ordinal);
        Assert.Contains($"event: {DiscoveryRunCompleted.Kind}", streamed, StringComparison.Ordinal);
    }

    /// <summary>A header naming a place this run never reached belongs to some other run, so the run is streamed whole.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a sequence")]
    [InlineData("-1")]
    [InlineData("9")]
    public async Task Watch_AHeaderNamingNoPlaceInThisRun_StreamsTheRunFromItsBeginning(string resumedFrom)
    {
        // Arrange
        var registry = NewRegistry();
        registry.TryOpen(SyntheticMailOwner.Deployment, out var journal);
        Assert.NotNull(journal);
        journal.Append(new DiscoveryRunStarted());
        journal.Append(new DiscoveryRunCompleted([]));

        // Act
        var streamed = await StreamedBody(journal, registry, resumedFrom);

        // Assert
        Assert.Contains("id: 1", streamed, StringComparison.Ordinal);
    }

    private static Results<Accepted<ClientDiscoveryRunResponse>, ProblemHttpResult> Start(
        ClientDiscoveryRunRequest request,
        DiscoveryRunRegistry registry) =>
        ClientDiscoveryRunEndpoints.Start(
            request,
            ResolverFor(SyntheticMailOwner.Deployment),
            AdmittedCaller(SyntheticMailOwner.Deployment),
            registry,
            LauncherOver(registry));

    private static Results<ServerSentEventsResult<DiscoveryRunEvent>, NotFound> Watch(
        Guid runId,
        DiscoveryRunRegistry registry,
        HttpContext? context = null) =>
        ClientDiscoveryRunEndpoints.Watch(
            runId,
            context ?? new DefaultHttpContext(),
            ResolverFor(SyntheticMailOwner.Deployment),
            registry);

    /// <summary>Runs the streaming result against a request stating where the caller left off, and reads what went out.</summary>
    /// <remarks>
    /// The result is executed rather than inspected, because what a client reads is the protocol's own framing — the
    /// identifier and the name each event carries — and that exists only once the result has written it.
    /// </remarks>
    private static async Task<string> StreamedBody(DiscoveryRunJournal journal, DiscoveryRunRegistry registry, string? resumedFrom)
    {
        var body = new MemoryStream();
        var context = ReadingContext(resumedFrom);
        context.Response.Body = body;

        var answered = Watch(journal.Id.Value, registry, context);
        await Assert.IsType<ServerSentEventsResult<DiscoveryRunEvent>>(answered.Result).ExecuteAsync(context);

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static DefaultHttpContext ReadingContext(string? resumedFrom)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };

        if (resumedFrom is not null)
        {
            context.Request.Headers["Last-Event-ID"] = resumedFrom;
        }

        return context;
    }

    private static DiscoveryRunRegistry NewRegistry() => new(new FakeTimeProvider(Now));

    private static IAuthorizedPrincipalSource AdmittedCaller(MailOwnerId owner)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(
            AuthorizedPrincipal.CallerActingFor(owner, "test-caller", [MailFathomPermission.MailAsk]));

        return principals;
    }

    private static MailboxScopeResolver ResolverFor(MailOwnerId owner) =>
        new(
            OwnedMailAccountCatalogs.For(
                AccessAuthorizations.ForOwnerGranted(owner, MailFathomPermission.MailAsk),
                SyntheticServedAccount.Of("primary", owner)),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing.Resolver);

    private static DiscoveryRunLauncher LauncherOver(DiscoveryRunRegistry registry) =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            registry,
            Substitute.For<IHostApplicationLifetime>(),
            NullLogger<DiscoveryRunLauncher>.Instance);
}
