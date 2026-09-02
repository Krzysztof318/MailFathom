// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers where a client still pointed at <c>http://</c> is sent, and when it is not sent anywhere.</summary>
/// <remarks>
/// The redirect exists so that enabling TLS does not read as an outage, and it is safe only because the listener serves
/// nothing else. Two rules here carry that weight: the host is redirected to itself rather than to a configured target,
/// so one listener serving several domains never sends a client to a name it did not ask for; and a host the deployment
/// does not publish is refused rather than rewritten.
/// </remarks>
public sealed class ClearTextRedirectToHttpsTests
{
    private const int RedirectListenerPort = 8080;

    private const int OtherSurfaceRedirectPort = 8091;

    private static readonly ClearTextRedirectTargets Targets = new(
    [
        new ClearTextRedirectListener(
            RedirectListenerPort,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["mail.example.test"] = 8443,
                ["managed.example.test"] = 9443,
                ["standard.example.test"] = 443,
            }),
        new ClearTextRedirectListener(
            OtherSurfaceRedirectPort,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["admin.example.test"] = 8543,
            }),
    ]);

    [Fact]
    public void ResolveLocation_APublishedDomain_RedirectsToItselfOverHttpsOnTheProfilePort() =>
        Assert.Equal(
            "https://mail.example.test:8443/mcp",
            ResolveFor("mail.example.test", "/mcp"));

    /// <summary>Preserved, so the redirect reaches the resource that was asked for rather than the surface's root.</summary>
    [Fact]
    public void ResolveLocation_APathAndQuery_AreCarriedThrough() =>
        Assert.Equal(
            "https://mail.example.test:8443/mcp/tools?cursor=abc&limit=20",
            ResolveFor("mail.example.test", "/mcp/tools", "?cursor=abc&limit=20"));

    /// <summary>
    /// Several domains share one clear-text listener, and each has to reach its own: a single resolved target would send
    /// every client to whichever profile configuration named first, under a certificate issued for a different name.
    /// </summary>
    [Fact]
    public void ResolveLocation_ASecondDomainOnTheSameListener_RedirectsToItsOwnProfilePort() =>
        Assert.Equal(
            "https://managed.example.test:9443/mcp",
            ResolveFor("managed.example.test", "/mcp"));

    /// <summary>Written without a port, because <c>:443</c> is the scheme's own and a client appends nothing for it.</summary>
    [Fact]
    public void ResolveLocation_AProfileOnTheSchemeDefaultPort_OmitsThePort() =>
        Assert.Equal(
            "https://standard.example.test/mcp",
            ResolveFor("standard.example.test", "/mcp"));

    /// <summary>A host name matches the way a client sends it, which is without regard to case.</summary>
    [Fact]
    public void ResolveLocation_APublishedDomainInAnotherCase_StillResolves() =>
        Assert.Equal(
            "https://Mail.Example.Test:8443/mcp",
            ResolveFor("Mail.Example.Test", "/mcp"));

    /// <summary>The port the client wrote is the clear-text one it is being moved off, so it never reaches the target.</summary>
    [Fact]
    public void ResolveLocation_AHostCarryingTheClearTextPort_RedirectsToTheProfilePortInstead() =>
        Assert.Equal(
            "https://mail.example.test:8443/mcp",
            ClearTextRedirectToHttps.ResolveLocation(
                Targets,
                RedirectListenerPort,
                new HostString("mail.example.test", RedirectListenerPort),
                "/mcp",
                QueryString.Empty));

    /// <summary>The name came from the client, and answering it with another configured domain would serve an identity nobody asked for.</summary>
    [Fact]
    public void ResolveLocation_AHostTheSurfaceDoesNotPublish_ResolvesToNothing() =>
        Assert.Null(ResolveFor("someone-elses-domain.test", "/mcp"));

    /// <summary>Each surface's listener publishes its own domains, so the other surface's are not reachable through it.</summary>
    [Fact]
    public void ResolveLocation_AnotherSurfacesDomainOnThisListener_ResolvesToNothing() =>
        Assert.Null(ResolveFor("admin.example.test", "/api/admin/session"));

    [Fact]
    public void ResolveLocation_ARequestWithoutAHostHeader_ResolvesToNothing() =>
        Assert.Null(ClearTextRedirectToHttps.ResolveLocation(
            Targets,
            RedirectListenerPort,
            default,
            "/mcp",
            QueryString.Empty));

    /// <summary>The administrative surface redirects to its own profiles, on its own listener.</summary>
    [Fact]
    public void ResolveLocation_TheOtherSurfacesListener_RedirectsToThatSurfacesProfile() =>
        Assert.Equal(
            "https://admin.example.test:8543/api/admin/session",
            ClearTextRedirectToHttps.ResolveLocation(
                Targets,
                OtherSurfaceRedirectPort,
                new HostString("admin.example.test"),
                "/api/admin/session",
                QueryString.Empty));

    [Fact]
    public async Task UseClearTextRedirectToHttps_ARequestOnARedirectListener_IsAnsweredWithoutReachingTheRestOfThePipeline()
    {
        // Arrange
        var context = RequestOn(RedirectListenerPort, "mail.example.test", "/mcp", "?cursor=abc");
        var reachedTheRestOfThePipeline = false;

        // Act
        await RedirectMiddleware(() => reachedTheRestOfThePipeline = true)(context);

        // Assert
        Assert.Equal(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
        Assert.Equal("https://mail.example.test:8443/mcp?cursor=abc", context.Response.Headers.Location);
        Assert.False(reachedTheRestOfThePipeline);
    }

    /// <summary>
    /// The whole safety property of this listener: nothing behind it runs. Were the refusal to fall through, the request
    /// would go on to reach routing, authentication, and the rate limiter over a clear-text hop.
    /// </summary>
    [Fact]
    public async Task UseClearTextRedirectToHttps_AnUnrecognizedHostOnARedirectListener_IsRefusedWithoutReachingTheRestOfThePipeline()
    {
        // Arrange
        var context = RequestOn(RedirectListenerPort, "someone-elses-domain.test", "/mcp");
        var reachedTheRestOfThePipeline = false;

        // Act
        await RedirectMiddleware(() => reachedTheRestOfThePipeline = true)(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.True(StringValues.IsNullOrEmpty(context.Response.Headers.Location));
        Assert.False(reachedTheRestOfThePipeline);
    }

    /// <summary>
    /// The direction that matters as much: this middleware is registered ahead of every route in the process, so a listener
    /// it claimed by mistake would take the whole surface down rather than redirect it.
    /// </summary>
    [Fact]
    public async Task UseClearTextRedirectToHttps_ARequestOnAListenerThatServesRoutes_ReachesTheRestOfThePipelineUntouched()
    {
        // Arrange
        var context = RequestOn(8443, "mail.example.test", "/mcp");
        var reachedTheRestOfThePipeline = false;

        // Act
        await RedirectMiddleware(() => reachedTheRestOfThePipeline = true)(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True(StringValues.IsNullOrEmpty(context.Response.Headers.Location));
        Assert.True(reachedTheRestOfThePipeline);
    }

    /// <summary>A deployment where no surface redirects leaves every listener alone, which is why the registration can be unconditional.</summary>
    [Fact]
    public async Task UseClearTextRedirectToHttps_NoSurfaceRedirecting_LeavesEveryRequestAlone()
    {
        // Arrange
        var context = RequestOn(RedirectListenerPort, "mail.example.test", "/mcp");
        var reachedTheRestOfThePipeline = false;

        // Act
        await RedirectMiddleware(() => reachedTheRestOfThePipeline = true, new ClearTextRedirectTargets([]))(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True(reachedTheRestOfThePipeline);
    }

    private static string? ResolveFor(string host, string path, string query = "") =>
        ClearTextRedirectToHttps.ResolveLocation(
            Targets,
            RedirectListenerPort,
            new HostString(host),
            new PathString(path),
            new QueryString(query));

    private static RequestDelegate RedirectMiddleware(Action onReached, ClearTextRedirectTargets? targets = null)
    {
        var pipeline = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        pipeline.UseClearTextRedirectToHttps(targets ?? Targets);
        pipeline.Run(_ =>
        {
            onReached();

            return Task.CompletedTask;
        });

        return pipeline.Build();
    }

    private static DefaultHttpContext RequestOn(int port, string host, string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = port;
        context.Request.Host = new HostString(host);
        context.Request.Path = new PathString(path);
        context.Request.QueryString = new QueryString(query);

        return context;
    }
}
