// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using MailFathom.Domain.Failures;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the configuration routes admit, what they refuse before anything is written, and under which grant.</summary>
/// <remarks>
/// <para>
/// What the write itself decides is covered against the administration and the writer beneath it, and is not repeated
/// here. What these routes decide is the part above: which request is a request at all, which of the two grants each
/// operation is published under, and whether a deployment that composed no persisted layer serves the routes at all.
/// </para>
/// <para>
/// The refusals are asserted for their status as much as for their text. A caller's mistake and a refusal about the
/// configuration are different answers on purpose — the first is a request to fix and the second is an outcome to act
/// on, carrying the version the next attempt is composed over — and collapsing the two would leave <c>mfctl</c> unable
/// to tell a typo from a document somebody else moved on.
/// </para>
/// </remarks>
public sealed class ConfigurationEndpointsTests
{
    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these paths from
    /// constants of its own, so a rename on either side compiles cleanly while every configuration command reaches a
    /// 404 that reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void ConfigurationRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/configuration", ConfigurationEndpoints.ConfigurationRoute);
        Assert.Equal("/configuration/document", ConfigurationEndpoints.DocumentRoute);
        Assert.Equal("/configuration/adoption", ConfigurationEndpoints.AdoptionRoute);
    }

    /// <summary>
    /// Reading is the administrative read and every write is the configuration grant, which is the whole reason the
    /// second name exists: the write that corrects a search bound is the write that widens a credential's grant.
    /// </summary>
    [Fact]
    public void MapConfiguration_PublishesReadsUnderTheAdministrativeGrantAndWritesUnderTheConfigurationOne()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");
        var endpoints = RouteBuilderServing(deployment);

        // Act
        endpoints.MapGroup("/admin").MapConfiguration();

        // Assert
        Assert.Equal(
            [
                "GET /admin/configuration -> mailfathom.admin.read",
                "POST /admin/configuration -> mailfathom.admin.configuration.write",
                "GET /admin/configuration/document -> mailfathom.admin.read",
                "POST /admin/configuration/document -> mailfathom.admin.configuration.write",
                "GET /admin/configuration/adoption -> mailfathom.admin.read",
                "POST /admin/configuration/adoption -> mailfathom.admin.configuration.write",
            ],
            PublishedAllocation(endpoints));
    }

    /// <summary>
    /// The bound on each body, which the routes carry as metadata the routing pipeline reads. Without it the server's
    /// own default applies, and a saved document is the one body here an authenticated client states the whole of.
    /// </summary>
    [Fact]
    public void MapConfiguration_EveryRouteThatReadsABody_CarriesTheRequestBodyBound()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");
        var endpoints = RouteBuilderServing(deployment);

        // Act
        endpoints.MapGroup("/admin").MapConfiguration();

        var writes = endpoints
            .Materialize()
            .OfType<RouteEndpoint>()
            .Where(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"))
            .ToArray();

        // Assert
        Assert.Equal(3, writes.Length);
        Assert.All(writes, route => Assert.Equal(
            ConfigurationEndpoints.MaxWriteRequestBytes,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize));
    }

    /// <summary>
    /// A host built from files alone serves none of these. The absence is what the mapping reads rather than what the
    /// first request discovers, so such a host answers <c>404</c> instead of failing to resolve a service.
    /// </summary>
    [Fact]
    public void MapConfiguration_ADeploymentComposingNoPersistedLayer_MapsNothing()
    {
        // Arrange
        var endpoints = new TestEndpointRouteBuilder(new ServiceCollection().AddRouting().BuildServiceProvider());

        // Act
        endpoints.MapGroup("/admin").MapConfiguration();

        // Assert
        Assert.Empty(endpoints.Materialize().OfType<RouteEndpoint>());
    }

    /// <summary>A reading reports the version it was composed over, because that is what the caller's next write states.</summary>
    [Fact]
    public void Read_ASettingTheDeploymentComposed_ReportsItWithTheVersionAWriteIsComposedOver()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}",
            version: 4);

        // Act
        var result = ConfigurationEndpoints.Read(deployment.Administration, "MailboxSearch");

        // Assert
        var reading = Assert.IsType<Ok<ConfigurationReadingResponse>>(result.Result);
        Assert.Equal(4, reading.Value!.Version);

        var setting = Assert.Single(reading.Value.Settings);
        Assert.Equal("MailboxSearch:SnippetsPerEmail", setting.Path);
        Assert.Equal("2", setting.Value);
        Assert.Equal(SettingSource.File.Name, setting.Source);
        Assert.False(setting.Redacted);
    }

    /// <summary>
    /// A path nobody configured is a fact about the deployment rather than an address that does not exist, and it is
    /// the answer an operator asking whether a setting is set at all wants.
    /// </summary>
    [Fact]
    public void Read_APathNothingSupplies_AnswersWithAnEmptyReadingRatherThanAMissingResource()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        // Act
        var result = ConfigurationEndpoints.Read(deployment.Administration, "MailboxSearch:SnippetsPerEmail");

        // Assert
        var reading = Assert.IsType<Ok<ConfigurationReadingResponse>>(result.Result);
        Assert.Empty(reading.Value!.Settings);
    }

    /// <summary>A secret-bearing setting leaves this surface as the marker, whichever route reports it.</summary>
    [Fact]
    public void Read_ASecretBearingSetting_ReportsTheMarkerAndSaysThatItDid()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "Chat": { "ApiKey": { "SecretReference": "file:/run/secrets/chat" } } }""",
            persisted: "{}");

        // Act
        var result = ConfigurationEndpoints.Read(deployment.Administration, "Chat:ApiKey:SecretReference");

        // Assert
        var setting = Assert.Single(Assert.IsType<Ok<ConfigurationReadingResponse>>(result.Result).Value!.Settings);
        Assert.Equal(SettingRedaction.Marker, setting.Value);
        Assert.True(setting.Redacted);
    }

    /// <summary>A write states at least one change, because a write that names none asks the deployment for nothing.</summary>
    [Fact]
    public async Task WriteAsync_ARequestNamingNoChange_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        // Act
        var result = await ConfigurationEndpoints.WriteAsync(
            deployment.Administration,
            new ConfigurationWriteRequest(Version: 1, Changes: [], EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A path the writer will not accept is a caller's mistake rather than an outcome to act on.</summary>
    [Fact]
    public async Task WriteAsync_AChangeNamingNoSetting_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        // Act
        var result = await ConfigurationEndpoints.WriteAsync(
            deployment.Administration,
            new ConfigurationWriteRequest(
                Version: 1,
                Changes: [new ConfigurationChangeRequest(Path: "   ", Value: "5")],
                EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>The bound the writer states is the bound this surface refuses past, so one number governs both.</summary>
    [Fact]
    public async Task WriteAsync_MoreChangesThanOneWriteCarries_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        var changes = Enumerable
            .Range(0, IConfigurationWriter.MaximumEdits + 1)
            .Select(index => new ConfigurationChangeRequest($"MailboxSearch:Setting{index}", "1"))
            .ToArray();

        // Act
        var result = await ConfigurationEndpoints.WriteAsync(
            deployment.Administration,
            new ConfigurationWriteRequest(Version: 1, Changes: changes, EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>
    /// A change naming one path twice is refused, because the two layers behind this route disagree about what it
    /// means: the writer applies the edits in the order given, and the administration drops an edit that would change
    /// nothing about the document as it stands — so the second of a pair would be dropped and the first committed,
    /// leaving the value the caller asked to be rid of.
    /// </summary>
    [Fact]
    public async Task WriteAsync_AChangeNamingOnePathTwice_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        // Act
        var result = await ConfigurationEndpoints.WriteAsync(
            deployment.Administration,
            new ConfigurationWriteRequest(
                Version: 1,
                Changes:
                [
                    new ConfigurationChangeRequest("MailboxSearch:SnippetsPerEmail", "9"),
                    new ConfigurationChangeRequest("mailboxsearch:snippetsperemail", "5"),
                ],
                EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(
            "MailboxSearch:SnippetsPerEmail",
            refusal.ProblemDetails.Detail ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A change the deployment binds commits, and the answer carries what the setting reads as on each side.</summary>
    [Fact]
    public async Task WriteAsync_AChangeTheConfigurationBinds_AnswersWithTheCommitAndBothReadings()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var result = await ConfigurationEndpoints.WriteAsync(
            deployment.Administration,
            new ConfigurationWriteRequest(
                Version: 1,
                Changes: [new ConfigurationChangeRequest("MailboxSearch:SnippetsPerEmail", "5")],
                EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ConfigurationWriteResponse>>(result.Result).Value!;
        Assert.True(written.Committed);
        Assert.Equal(2, written.Version);
        Assert.Null(written.Code);

        var change = Assert.Single(written.Changes);
        Assert.Equal("2", change.Before?.Value);
        Assert.Equal("5", change.After?.Value);
    }

    /// <summary>
    /// A refusal about the configuration itself is an outcome rather than an error, because the administrator acts on
    /// it and composes the next attempt over the version it carries.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ASettingAnOverrideSupplies_AnswersWithTheRefusalCodeAndTheVersionInForce()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var result = await ConfigurationEndpoints.WriteAsync(
            deployment.Administration,
            new ConfigurationWriteRequest(
                Version: 1,
                Changes: [new ConfigurationChangeRequest("MailboxSearch:SnippetsPerEmail", "5")],
                EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<Ok<ConfigurationWriteResponse>>(result.Result).Value!;
        Assert.False(refused.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationWriteShadowed.Value, refused.Code);
        Assert.Equal(1, refused.Version);
        Assert.NotEmpty(refused.Messages);
    }

    /// <summary>The document leaves with the version it was read at, which is what the save that follows is judged against.</summary>
    [Fact]
    public async Task ReadDocumentAsync_ADocumentCarryingASecret_HandsOverTheMarkerAndTheVersionItWasReadAt()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: """{ "Chat": { "ApiKey": { "SecretReference": "file:/run/secrets/chat" } } }""",
            version: 3);

        // Act
        var result = await ConfigurationEndpoints.ReadDocumentAsync(
            deployment.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Value!.Version);
        Assert.DoesNotContain("/run/secrets/chat", result.Value.Document, StringComparison.Ordinal);
        Assert.Contains(SettingRedaction.Marker, result.Value.Document, StringComparison.Ordinal);
    }

    /// <summary>An editing session that means to change nothing sends nothing, so an empty body is a caller's mistake.</summary>
    [Fact]
    public async Task SaveDocumentAsync_ARequestCarryingNoDocument_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        // Act
        var result = await ConfigurationEndpoints.SaveDocumentAsync(
            deployment.Administration,
            new ConfigurationDocumentRequest(Version: 1, Document: null, EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A saved document commits as one transaction over the version the buffer was opened at.</summary>
    [Fact]
    public async Task SaveDocumentAsync_AnEditedDocument_CommitsOverTheVersionItWasOpenedAt()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        // Act
        var result = await ConfigurationEndpoints.SaveDocumentAsync(
            deployment.Administration,
            new ConfigurationDocumentRequest(
                Version: 1,
                Document: """{ "MailboxSearch": { "SnippetsPerEmail": "7" } }""",
                EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ConfigurationWriteResponse>>(result.Result).Value!;
        Assert.True(written.Committed);
        Assert.Equal(2, written.Version);
        Assert.Equal(1, deployment.Row.AcceptedCommits);
    }

    /// <summary>A version somebody else moved past is an outcome the operator reopens the buffer from, not an error.</summary>
    [Fact]
    public async Task SaveDocumentAsync_AVersionAnotherWriterMovedPast_AnswersWithTheRefusalAndTheVersionInForce()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        deployment.Row.CommitFromElsewhere("""{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var result = await ConfigurationEndpoints.SaveDocumentAsync(
            deployment.Administration,
            new ConfigurationDocumentRequest(
                Version: 1,
                Document: """{ "MailboxSearch": { "SnippetsPerEmail": "7" } }""",
                EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<Ok<ConfigurationWriteResponse>>(result.Result).Value!;
        Assert.False(refused.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded.Value, refused.Code);
        Assert.Equal(2, refused.Version);
    }

    /// <summary>There is no adoption of the whole configuration, so a preview naming no path is refused before it reads one.</summary>
    [Fact]
    public void ReadAdoptable_APreviewNamingNoPath_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        // Act
        var result = ConfigurationEndpoints.ReadAdoptable(deployment.Administration, prefix: null);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>The preview names the file that stops deciding a value, which is the part an operator weighs.</summary>
    [Fact]
    public void ReadAdoptable_APathTheFilesDecide_ReportsWhatAnAdoptionWouldCopyAndFromWhere()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var result = ConfigurationEndpoints.ReadAdoptable(deployment.Administration, "MailboxSearch");

        // Assert
        var setting = Assert.Single(Assert.IsType<Ok<ConfigurationReadingResponse>>(result.Result).Value!.Settings);
        Assert.Equal("MailboxSearch:SnippetsPerEmail", setting.Path);
        Assert.Equal(SettingSource.File.Name, setting.Source);
        Assert.Equal(ComposedConfigurationDeployment.ProvisionedFileName, setting.Origin);
    }

    /// <summary>An adoption commits what the files decide, and the layer then decides it instead.</summary>
    [Fact]
    public async Task AdoptAsync_APathTheFilesDecide_CommitsThoseValuesIntoTheLayer()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var result = await ConfigurationEndpoints.AdoptAsync(
            deployment.Administration,
            new ConfigurationAdoptionRequest(Version: 1, Prefix: "MailboxSearch", EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        var adopted = Assert.IsType<Ok<ConfigurationWriteResponse>>(result.Result).Value!;
        Assert.True(adopted.Committed);
        Assert.Equal(SettingSource.PersistedLayer.Name, Assert.Single(adopted.Changes).After?.Source);
    }

    /// <summary>An adoption naming no path is refused before it reads one, exactly as its preview is.</summary>
    [Fact]
    public async Task AdoptAsync_ARequestNamingNoPath_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");

        // Act
        var result = await ConfigurationEndpoints.AdoptAsync(
            deployment.Administration,
            new ConfigurationAdoptionRequest(Version: 1, Prefix: " ", EvenIfShadowed: false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A version below zero is no version any writer produced, so every write refuses it before it reads a row.</summary>
    [Fact]
    public async Task EveryWrite_AVersionNoWriterProduced_IsRefusedAsTheCallersMistake()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}");
        var administration = deployment.Administration;
        var cancellation = TestContext.Current.CancellationToken;

        // Act
        var keyed = await ConfigurationEndpoints.WriteAsync(
            administration,
            new ConfigurationWriteRequest(
                Version: -1,
                Changes: [new ConfigurationChangeRequest("MailboxSearch:SnippetsPerEmail", "5")],
                EvenIfShadowed: false),
            cancellation);

        var saved = await ConfigurationEndpoints.SaveDocumentAsync(
            administration,
            new ConfigurationDocumentRequest(Version: -1, Document: "{}", EvenIfShadowed: false),
            cancellation);

        var adopted = await ConfigurationEndpoints.AdoptAsync(
            administration,
            new ConfigurationAdoptionRequest(Version: -1, Prefix: "MailboxSearch", EvenIfShadowed: false),
            cancellation);

        // Assert
        Assert.IsType<ProblemHttpResult>(keyed.Result);
        Assert.IsType<ProblemHttpResult>(saved.Result);
        Assert.IsType<ProblemHttpResult>(adopted.Result);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>Reads back what each mapped route decided, as one line per verb and path.</summary>
    private static IEnumerable<string> PublishedAllocation(TestEndpointRouteBuilder endpoints) => endpoints
        .Materialize()
        .OfType<RouteEndpoint>()
        .SelectMany(
            endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods,
            (endpoint, method) => $"{method} /{endpoint.RoutePattern.RawText?.TrimStart('/')} -> {Describe(endpoint)}");

    /// <summary>Names what a route decided, with the route that decided on none saying so.</summary>
    private static string Describe(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<RoutePermission>() is { Permission.IsSpecified: true } published
            ? published.Permission.Name
            : "none";

    /// <summary>Builds the routing seam the mapping extends, serving the administration a deployment composed.</summary>
    private static TestEndpointRouteBuilder RouteBuilderServing(ComposedConfigurationDeployment deployment)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddSingleton(deployment.Administration);

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }
}
