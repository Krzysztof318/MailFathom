// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace MailFathom.Host.Api.Documentation;

/// <summary>Publishes one OpenAPI document for the whole HTTP API, and the explorer a developer reads it in.</summary>
/// <remarks>
/// <para>
/// One document rather than one per surface, deliberately. The administrative and the client routes answer on separate
/// listeners under separate credentials, but what a developer is holding is a single question — what does this
/// deployment serve over HTTP — and two documents would answer it only when both were opened. The surfaces stay apart
/// where separation is enforced, which is the listener, the credential, and the permission; a description of them is
/// not a place that enforcement lives.
/// </para>
/// <para>
/// Both routes exist only while the environment is exactly <c>Development</c>, and the rule is enforced here rather
/// than at the two call sites so that registration and mapping cannot come apart: a document generated for a
/// production process would be a route somebody could later map by accident, and a route mapped without the document
/// registered would answer <c>500</c> instead of not existing. Outside Development neither service nor endpoint is
/// created at all, so the paths answer <c>404</c> because nothing is there — not <c>401</c>, which would still confirm
/// the catalogue exists.
/// </para>
/// <para>
/// The routes admit anybody, and the operations they describe do not. Reading the document is reading a contract, which
/// discloses nothing a caller could not learn by trying the routes; every request the explorer then makes travels the
/// ordinary pipeline and meets the same authentication, authorization, limits, and timeouts as any other client's.
/// <see cref="ApiDocumentSecurity" /> is what keeps the description of that honest.
/// </para>
/// </remarks>
internal static class ApiDocumentation
{
    /// <summary>The one document this host generates, named in its route and in what the explorer loads.</summary>
    /// <remarks>Deterministic rather than derived from the running version: a document name is what a reader bookmarks and what a generated client is pointed at, and moving it every release would break both to say something the document's own <c>info.version</c> already says.</remarks>
    internal const string DocumentName = "v1";

    /// <summary>The route template the document is served from.</summary>
    /// <remarks>The framework's own default. Keeping it is what lets a reader who knows ASP.NET Core find the document without being told where it is.</remarks>
    internal const string DocumentRoute = "/openapi/{documentName}.json";

    /// <summary>The address the one generated document actually answers at.</summary>
    /// <remarks>Stated beside the template rather than composed by a reader, because it is what documentation names and what a test asserts.</remarks>
    internal const string DocumentPath = "/openapi/" + DocumentName + ".json";

    /// <summary>The route the API explorer is served from.</summary>
    internal const string ExplorerRoute = "/scalar";

    /// <summary>What the operations the <c>mfctl</c> command reaches are filed under.</summary>
    internal const string AdministrativeSurfaceTag = "Administrative";

    /// <summary>What the operations the MailFathom client reaches are filed under.</summary>
    internal const string ClientSurfaceTag = "Client";

    /// <summary>The first segment of every path the document is served beneath.</summary>
    private static readonly PathString DocumentPathPrefix = new("/openapi");

    /// <summary>The first segment of every path the explorer serves, which includes the assets it loads.</summary>
    private static readonly PathString ExplorerPathPrefix = new(ExplorerRoute);

    /// <summary>Reports whether this process publishes the documentation surface at all.</summary>
    /// <param name="environment">The environment this process was started in.</param>
    /// <returns><see langword="true" /> in <c>Development</c>, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="environment" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The rule is stated once and read by everything that depends on it — the registration, the mapping, and the
    /// listener isolation that has to decide whether these paths exist before it decides which listeners serve them.
    /// Three separate environment checks would be three places to change it, and a documentation surface half of the
    /// process believed in is exactly the disagreement this class exists to prevent.
    /// </remarks>
    internal static bool IsPublishedIn(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsDevelopment();
    }

    /// <summary>Registers OpenAPI generation for the HTTP API, in Development and nowhere else.</summary>
    /// <param name="services">The service collection being composed.</param>
    /// <param name="environment">The environment this process was started in.</param>
    /// <returns>The same collection, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static IServiceCollection AddApiDocumentation(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        if (!IsPublishedIn(environment))
        {
            return services;
        }

        services.AddOpenApi(DocumentName, options =>
        {
            options.ShouldInclude = DescribesHttpApi;

            // Each transformer is adapted to the framework's delegate here rather than written in its shape, so that
            // the three below take what they read and nothing else, and are called from a test the same way.
            options.AddDocumentTransformer((document, _, _) =>
            {
                DescribeTheApi(document);

                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, _, _) =>
            {
                ApiDocumentSecurity.DeclareCredentialScheme(document);

                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                ApiDocumentSecurity.RequireCredentialWhereProtected(operation, context);

                return Task.CompletedTask;
            });

            // Last of the document transformers, because it rewrites what the operation pass produced.
            options.AddDocumentTransformer((document, _, _) =>
            {
                GroupOperationsBySurface(document);

                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>Names the API the document describes, and the release it was generated from.</summary>
    /// <param name="document">The document being generated.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Written rather than left to the framework, which names a document after the entry assembly and versions it
    /// <c>1.0.0</c>. Both are wrong here: the assembly name is an implementation detail a reader has no use for, and a
    /// version that is not this product's is worse than none, because a document is exactly the artifact somebody
    /// compares between releases. The version is the one every surface already reports about itself, read the same way
    /// the session responses read it.
    /// </remarks>
    internal static void DescribeTheApi(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "MailFathom HTTP API",
            Version = StampedAssemblyVersion.ReadFrom(typeof(ApiDocumentation).Assembly).Version,
            Description =
                "Every operation MailFathom serves over HTTP, across both of its API surfaces. The administrative "
                + "surface is what the mfctl command reaches; the client surface is what the MailFathom client "
                + "reaches. Each answers on a listener of its own under credentials of its own, and a deployment "
                + "serves only the surfaces its operator enabled — so an operation described here answers only where "
                + "the surface it belongs to is published.",
        };
    }

    /// <summary>Files every operation under the surface that serves it, and publishes those two tags and no others.</summary>
    /// <param name="document">The document being generated, with every operation already added to it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The framework tags an operation with the name of the type that mapped it, which puts `ContactEndpoints` and
    /// `MailFathom.Host` into a published contract. Both are implementation details a reader has no use for, and both
    /// would move a document that is otherwise stable whenever an internal type was renamed. What a reader is actually
    /// sorting by is which surface answers, which is exactly the distinction the two route prefixes already carry.
    /// </remarks>
    internal static void GroupOperationsBySurface(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operationsByPath = (document.Paths ?? [])
            .Where(static path => path.Value.Operations is not null)
            .SelectMany(path => path.Value.Operations!.Values.Select(operation => (Path: path.Key, Operation: operation)));

        foreach (var (path, operation) in operationsByPath)
        {
            operation.Tags = new HashSet<OpenApiTagReference>
            {
                new(SurfaceTagOf(path), document),
            };
        }

        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = AdministrativeSurfaceTag },
            new() { Name = ClientSurfaceTag },
        };
    }

    /// <summary>Names the surface a documented path is served by.</summary>
    /// <remarks>A two-way split rather than a lookup with a fallback, because <see cref="DescribesHttpApi" /> has already reduced the input to exactly two prefixes: a path that is not the administrative surface's is the client surface's, and there is no third case for a third branch to name.</remarks>
    private static string SurfaceTagOf(string path) =>
        new PathString(path).StartsWithSegments(AdminEndpointOptions.RoutePrefix, StringComparison.OrdinalIgnoreCase)
            ? AdministrativeSurfaceTag
            : ClientSurfaceTag;

    /// <summary>Maps the document and the explorer, in Development and nowhere else.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="environment">The environment this process was started in.</param>
    /// <returns>The same route builder, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Both routes are mapped outside every surface group, which is what keeps them from inheriting a requirement, a
    /// rate-limiting policy, or a request timeout belonging to a surface they describe rather than sit on. Anonymous
    /// access is then stated rather than left to the absence of a requirement, so a fallback policy added later cannot
    /// quietly close the one route a developer reads before they hold any credential.
    /// </remarks>
    internal static IEndpointRouteBuilder MapApiDocumentation(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        if (!IsPublishedIn(environment))
        {
            return endpoints;
        }

        endpoints.MapOpenApi(DocumentRoute).AllowAnonymous();

        endpoints.MapScalarApiReference(
                ExplorerRoute,
                options => options
                    .WithOpenApiRoutePattern(DocumentRoute)
                    // The script bundle already comes out of the assembly; the typeface is the one asset the package
                    // still fetches from a content delivery network, and a developer working offline is exactly who
                    // this endpoint exists for.
                    .DisableDefaultFonts())
            .AllowAnonymous();

        return endpoints;
    }

    /// <summary>Reports whether a request path belongs to the documentation surface.</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true" /> when the path is the document's or the explorer's.</returns>
    /// <remarks>
    /// Matched by segment rather than by prefix string, so a route such as <c>/scalars</c> is not mistaken for one of
    /// these, and by prefix rather than exactly, because the explorer serves its own assets beneath its route.
    /// </remarks>
    internal static bool IsDocumentationPath(PathString path) =>
        path.StartsWithSegments(DocumentPathPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(ExplorerPathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports whether an endpoint the app mapped belongs to the HTTP API this document describes.</summary>
    /// <param name="description">What the API explorer reports about one endpoint.</param>
    /// <returns><see langword="true" /> when the endpoint is served beneath the administrative or the client route prefix.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="description" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An allow-list of the two prefixes rather than a deny-list of what to leave out, because everything else this
    /// process maps is infrastructure whose contract is published elsewhere or not at all: the MCP protocol route,
    /// whose tools are described by the protocol itself, the attachment download a signed link admits, the health
    /// probes, and the two RFC 9728 metadata documents, which are discovery for a client that holds no credential yet
    /// rather than operations anybody calls. A route added to one of those later stays out without anybody amending
    /// this, which is the direction the mistake should fall in.
    /// </remarks>
    internal static bool DescribesHttpApi(ApiDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        if (description.RelativePath is not { } relativePath)
        {
            return false;
        }

        var path = new PathString("/" + relativePath.TrimStart('/'));

        return path.StartsWithSegments(AdminEndpointOptions.RoutePrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(ClientEndpointOptions.RoutePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
