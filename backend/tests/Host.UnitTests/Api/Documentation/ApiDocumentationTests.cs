// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Host.Api.Documentation;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api.Documentation;

/// <summary>Covers when the documentation surface exists at all, what admits a caller to it, and what it describes.</summary>
/// <remarks>
/// The three are one subject rather than three: the surface is unauthenticated, so whether it exists is the whole of
/// its protection, and what it includes is what an anonymous reader is handed. Each is read from the mapping and the
/// registration rather than from a started server, which is the boundary <c>backend/tests/AGENTS.md</c> draws.
/// </remarks>
public sealed class ApiDocumentationTests
{
    /// <summary>Environment names that are not <c>Development</c>, including one nobody planned for.</summary>
    /// <remarks>A custom name matters as much as the two the framework ships with: the rule is that Development is the only environment with a documentation surface, not that Production and Staging are the two without one.</remarks>
    public static TheoryData<string> NonDevelopmentEnvironments => new("Production", "Staging", "Integration");

    /// <summary>The two routes the operations documentation names, at the addresses it names them at.</summary>
    /// <remarks>The explorer's own pattern carries an optional document name, so <c>/scalar</c> reaches it and so does <c>/scalar/v1</c>.</remarks>
    [Fact]
    public void MapApiDocumentation_InDevelopment_ServesTheDocumentAndTheExplorer()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapApiDocumentation(EnvironmentNamed(Environments.Development));

        // Assert
        var routes = MappedRoutes(endpoints);

        Assert.Contains(ApiDocumentation.DocumentRoute, routes, StringComparer.Ordinal);
        Assert.Contains($"{ApiDocumentation.ExplorerRoute}/{{documentName?}}", routes, StringComparer.Ordinal);
    }

    /// <summary>
    /// The explorer's script is served by this process rather than fetched from a content delivery network, which is
    /// what makes the page work on a development machine with no route out and keeps a third party off the path
    /// between a developer and their own deployment.
    /// </summary>
    [Fact]
    public void MapApiDocumentation_InDevelopment_ServesTheExplorersOwnAssets()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapApiDocumentation(EnvironmentNamed(Environments.Development));

        // Assert
        Assert.Contains(MappedRoutes(endpoints), static route => route.EndsWith(".js", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every route this maps is one the listener isolation recognizes as documentation. Without that the explorer
    /// would load its own script from a path the isolation middleware refuses, and the page would arrive blank on
    /// every listener that does not serve the MCP surface.
    /// </summary>
    [Fact]
    public void MapApiDocumentation_InDevelopment_MapsNothingOutsideTheDocumentationPaths()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapApiDocumentation(EnvironmentNamed(Environments.Development));

        // Assert
        var routes = MappedRoutes(endpoints);
        Assert.NotEmpty(routes);
        Assert.All(routes, static route => Assert.True(ApiDocumentation.IsDocumentationPath(new PathString(route))));
    }

    /// <summary>The regression this exists for: a fallback authorization policy added later would otherwise close the one route a developer reads before they hold any credential.</summary>
    [Fact]
    public void MapApiDocumentation_InDevelopment_AdmitsAnAnonymousReaderToEveryRoute()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapApiDocumentation(EnvironmentNamed(Environments.Development));

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(mapped, static endpoint => Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>()));
        Assert.All(mapped, static endpoint => Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    /// <summary>Outside Development the routes are absent rather than refused, which is why the paths answer 404 with no endpoint behind them.</summary>
    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void MapApiDocumentation_OutsideDevelopment_MapsNothing(string environmentName)
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapApiDocumentation(EnvironmentNamed(environmentName));

        // Assert
        Assert.Empty(endpoints.Materialize());
    }

    /// <summary>The other half of the same rule: nothing generates a document outside Development either, so no route could be mapped by accident later.</summary>
    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void AddApiDocumentation_OutsideDevelopment_RegistersNothing(string environmentName)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddApiDocumentation(EnvironmentNamed(environmentName));

        // Assert
        Assert.Empty(services);
    }

    /// <summary>The control for the test above, without which an assertion of emptiness would pass against a method that registered nothing anywhere.</summary>
    [Fact]
    public void AddApiDocumentation_InDevelopment_RegistersTheGenerator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddApiDocumentation(EnvironmentNamed(Environments.Development));

        // Assert
        Assert.NotEmpty(services);
    }

    /// <summary>
    /// The framework would otherwise name the document after the entry assembly and version it <c>1.0.0</c>. The
    /// second is the one that matters: a document is the artifact somebody compares between releases, so a version
    /// that is not this product's is worse than none.
    /// </summary>
    [Fact]
    public void DescribeTheApi_Always_NamesTheProductAndTheVersionItWasBuiltFrom()
    {
        // Arrange
        var document = new OpenApiDocument();
        var stampedVersion = typeof(ApiDocumentation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0];

        // Act
        ApiDocumentation.DescribeTheApi(document);

        // Assert
        var info = Assert.IsType<OpenApiInfo>(document.Info);
        Assert.Equal(stampedVersion, info.Version);
        Assert.DoesNotContain(
            typeof(ApiDocumentation).Assembly.GetName().Name!,
            info.Title ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The framework tags an operation with the name of the type that mapped it, which puts internal type names into a
    /// published contract and moves the document whenever one is renamed. A reader sorts by which surface answers.
    /// </summary>
    [Fact]
    public void GroupOperationsBySurface_Always_FilesEachOperationUnderTheSurfaceThatServesIt()
    {
        // Arrange
        var document = DocumentDescribing(
            $"{AdminEndpointOptions.RoutePrefix}/session",
            $"{ClientEndpointOptions.RoutePrefix}/session");

        // Act
        ApiDocumentation.GroupOperationsBySurface(document);

        // Assert
        Assert.Equal(
            [ApiDocumentation.AdministrativeSurfaceTag, ApiDocumentation.ClientSurfaceTag],
            document.Paths!.Values
                .SelectMany(static path => path.Operations!.Values)
                .SelectMany(static operation => operation.Tags!.Select(static tag => tag.Name ?? string.Empty))
                .ToArray());

        Assert.Equal(
            [ApiDocumentation.AdministrativeSurfaceTag, ApiDocumentation.ClientSurfaceTag],
            document.Tags!.Select(static tag => tag.Name ?? string.Empty).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Every operation both API surfaces publish is described, whichever routes those come to be.</summary>
    [Theory]
    [InlineData("api/admin/session")]
    [InlineData("api/admin/accounts/{accountName}/synchronization")]
    [InlineData("api/client/session")]
    public void DescribesHttpApi_ForAnOperationOnEitherSurface_IncludesIt(string relativePath)
    {
        // Act
        var included = ApiDocumentation.DescribesHttpApi(new ApiDescription { RelativePath = relativePath });

        // Assert
        Assert.True(included);
    }

    /// <summary>The infrastructure routes this process also maps stay out, because none of them is an operation with a published HTTP contract.</summary>
    /// <remarks>
    /// The last two are the shapes a prefix comparison written against strings rather than segments would wrongly
    /// admit, which is why they are asserted beside the routes that genuinely exist.
    /// </remarks>
    [Theory]
    [InlineData("mcp")]
    [InlineData("attachments/{capability}")]
    [InlineData("health/ready")]
    [InlineData(".well-known/oauth-protected-resource/api/admin")]
    [InlineData("apiary")]
    [InlineData("api/clients")]
    public void DescribesHttpApi_ForAnythingElseTheHostMaps_LeavesItOut(string relativePath)
    {
        // Act
        var included = ApiDocumentation.DescribesHttpApi(new ApiDescription { RelativePath = relativePath });

        // Assert
        Assert.False(included);
    }

    /// <summary>The document and everything the explorer loads beneath its route are the documentation surface.</summary>
    [Theory]
    [InlineData(ApiDocumentation.DocumentPath)]
    [InlineData(ApiDocumentation.ExplorerRoute)]
    [InlineData(ApiDocumentation.ExplorerRoute + "/v1")]
    public void IsDocumentationPath_ForThePathsTheSurfaceServes_RecognizesThem(string path)
    {
        // Act
        var recognized = ApiDocumentation.IsDocumentationPath(new PathString(path));

        // Assert
        Assert.True(recognized);
    }

    /// <summary>A path merely beginning with the same letters belongs to whichever surface owns it, which for these is the MCP catch-all.</summary>
    [Theory]
    [InlineData("/scalars")]
    [InlineData("/openapidocs")]
    [InlineData(AdminEndpointOptions.RoutePrefix + "/session")]
    public void IsDocumentationPath_ForAPathTheSurfaceDoesNotServe_RejectsIt(string path)
    {
        // Act
        var recognized = ApiDocumentation.IsDocumentationPath(new PathString(path));

        // Assert
        Assert.False(recognized);
    }

    /// <summary>A document carrying one GET per path, tagged the way the framework tags one.</summary>
    private static OpenApiDocument DocumentDescribing(params string[] paths)
    {
        var document = new OpenApiDocument { Paths = [] };

        foreach (var path in paths)
        {
            document.Paths[path] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [HttpMethod.Get] = new() { Tags = new HashSet<OpenApiTagReference> { new("MailFathom.Host", document) } },
                },
            };
        }

        return document;
    }

    private static IReadOnlyList<string> MappedRoutes(TestEndpointRouteBuilder endpoints) =>
    [
        .. endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Select(static endpoint => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}"),
    ];

    private static IHostEnvironment EnvironmentNamed(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        return environment;
    }

    private static TestEndpointRouteBuilder BuildRouteBuilder()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddApiDocumentation(EnvironmentNamed(Environments.Development));

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }
}
