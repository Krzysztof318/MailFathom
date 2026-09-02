// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
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

/// <summary>Covers the one route that disposes of stored mail: what it erases, and what it refuses to.</summary>
/// <remarks>
/// The refusal is as much the contract as the erasure. Nothing else in MailFathom takes a folder's local copy away, so
/// this route is reached by an operator who means it — and a folder the account still mirrors is the one case where
/// meaning it would buy a remirror rather than the storage back, which is why it is refused rather than performed
/// carefully.
/// </remarks>
public sealed class MailFolderErasureEndpointTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>The account as the mirror store is asked about it, which is the owner and the identifier together.</summary>
    private static readonly MailAccountIdentity AccountIdentity =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Account);
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private readonly IMailFolderMappingReader mappings = Substitute.For<IMailFolderMappingReader>();

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes this path from a
    /// constant of its own, and a rename on either side compiles cleanly while the erase command reaches a 404 that
    /// reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void ErasureRoute_IsThePathTheCommandComposes() =>
        Assert.Equal("/folders/erasure", MailFolderErasureEndpoint.ErasureRoute);

    /// <summary>The bound on the body, which the route carries as metadata the routing pipeline reads.</summary>
    [Fact]
    public void MapMailFolderErasure_TheRoute_CarriesTheRequestBodyBound()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(string.Empty).MapMailFolderErasure();

        // Assert
        var route = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == MailFolderErasureEndpoint.ErasureRoute);

        Assert.Equal(["POST"], route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(
            MailFolderErasureEndpoint.MaxErasureRequestBytes,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>A folder switched off stopped being refreshed, and this is what turns that into the storage back.</summary>
    [Fact]
    public async Task EraseAsync_AMappedFolderNothingMirrors_ErasesOneBoundedPassOfIt()
    {
        // Arrange
        this.Maps(MailFolderMapping.ToSpecialUse(
            Archive,
            MailFolderSpecialUse.Archive,
            MailFolderParticipation.MappedOnly));

        var store = new RecordingMirrorStore(new MailFolderMirrorErasure(ErasedEmailCount: 500, EmailsRemain: true));

        // Act
        var result = await this.EraseAsync(store, new MailFolderErasureRequest("work", "archive"));

        // Assert
        var erasure = Assert.IsType<Ok<MailFolderErasureResponse>>(result.Result);
        Assert.Equal((Account.Value, Archive.Value, 500, true), (
            erasure.Value!.Account,
            erasure.Value.Folder,
            erasure.Value.ErasedEmailCount,
            erasure.Value.EmailsRemain));
        Assert.Equal([(AccountIdentity, Archive)], store.Passes);
    }

    /// <summary>
    /// The case no configuration value can express, and the one that most needs erasing: a mapping an operator removed
    /// leaves the rows behind deliberately, so a folder nothing names has to stay erasable.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnAliasNoMappingNames_ErasesItRatherThanRefusingTheRequest()
    {
        // Arrange
        this.Maps(mapping: null);

        var store = new RecordingMirrorStore(new MailFolderMirrorErasure(ErasedEmailCount: 7, EmailsRemain: false));

        // Act
        var result = await this.EraseAsync(store, new MailFolderErasureRequest("work", "archive"));

        // Assert
        var erasure = Assert.IsType<Ok<MailFolderErasureResponse>>(result.Result);
        Assert.Equal(7, erasure.Value!.ErasedEmailCount);
        Assert.Equal([(AccountIdentity, Archive)], store.Passes);
    }

    /// <summary>Erasing a folder a run is about to visit is a hole the next run silently refills, so it is refused.</summary>
    [Fact]
    public async Task EraseAsync_AFolderTheAccountStillMirrors_RefusesAndErasesNothing()
    {
        // Arrange
        this.Maps(MailFolderMapping.ToSpecialUse(Archive, MailFolderSpecialUse.Archive));

        var store = new RecordingMirrorStore(MailFolderMirrorErasure.Nothing);

        // Act
        var result = await this.EraseAsync(store, new MailFolderErasureRequest("work", "archive"));

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(Archive.Value, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Contains("Synchronize", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(store.Passes);
    }

    /// <summary>Running it again after the folder is empty is the ordinary end of every erasure, not an error.</summary>
    [Fact]
    public async Task EraseAsync_AFolderHoldingNothing_SucceedsHavingErasedNothing()
    {
        // Arrange
        this.Maps(mapping: null);

        var store = new RecordingMirrorStore(MailFolderMirrorErasure.Nothing);

        // Act
        var result = await this.EraseAsync(store, new MailFolderErasureRequest("work", "archive"));

        // Assert
        var erasure = Assert.IsType<Ok<MailFolderErasureResponse>>(result.Result);
        Assert.Equal(0, erasure.Value!.ErasedEmailCount);
        Assert.False(erasure.Value.EmailsRemain);
    }

    /// <summary>An account this deployment does not serve is a mistake in the request rather than a missing resource.</summary>
    [Theory]
    [InlineData(null, "archive")]
    [InlineData("", "archive")]
    [InlineData("personal", "archive")]
    public async Task EraseAsync_AnAccountThisDeploymentDoesNotServe_RefusesWithoutReachingTheEraser(
        string? account,
        string folder)
    {
        // Arrange
        var store = new RecordingMirrorStore(MailFolderMirrorErasure.Nothing);

        // Act
        var result = await this.EraseAsync(store, new MailFolderErasureRequest(account, folder));

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(store.Passes);
    }

    /// <summary>
    /// Text the alias type refuses reaches a stated refusal rather than a failure the process reports as its own,
    /// which is what a body an operator typed is owed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("arch\tive")]
    public async Task EraseAsync_TextThatNamesNoFolder_RefusesWithoutReachingTheEraser(string? folder)
    {
        // Arrange
        var store = new RecordingMirrorStore(MailFolderMirrorErasure.Nothing);

        // Act
        var result = await this.EraseAsync(store, new MailFolderErasureRequest("work", folder));

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(store.Passes);
    }

    private void Maps(MailFolderMapping? mapping) =>
        this.mappings.FindFolderNamed(Account, Archive).Returns(mapping);

    private Task<Results<Ok<MailFolderErasureResponse>, ProblemHttpResult>> EraseAsync(
        IStoredMailFolderMirrorStore store,
        MailFolderErasureRequest request) =>
        MailFolderErasureEndpoint.EraseAsync(
            request,
            CatalogServing(Account),
            this.mappings,
            EraserOver(store),
            TestContext.Current.CancellationToken);

    private static UnmirroredMailFolderEraser EraserOver(IStoredMailFolderMirrorStore store)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new UnmirroredMailFolderEraser(
            store,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            new MailboxSynchronizationOptions(),
            AdministrativeGrant.WholeSurface);
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

    /// <summary>Records which folder each pass was asked to erase, and answers with the erasure the test arranged.</summary>
    private sealed class RecordingMirrorStore(MailFolderMirrorErasure erasure) : IStoredMailFolderMirrorStore
    {
        private readonly List<(MailAccountIdentity Account, MailFolderAlias FolderAlias)> passes = [];

        public IReadOnlyList<(MailAccountIdentity Account, MailFolderAlias FolderAlias)> Passes => this.passes;

        public Task<MailFolderMirrorErasure> EraseFolderMirrorAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            MailFolderAlias folderAlias,
            int maxEmails,
            CancellationToken cancellationToken)
        {
            this.passes.Add((account, folderAlias));

            return Task.FromResult(erasure);
        }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
