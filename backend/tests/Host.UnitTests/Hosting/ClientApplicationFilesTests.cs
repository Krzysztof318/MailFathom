// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers how a deployment is recognized as carrying a client to serve, and where it serves it.</summary>
/// <remarks>
/// The bundle is copied into the container image at build time and by nothing at run time, so a host started from
/// anything else has the directory and none of the files. Recognizing that by the entry document is what turns an
/// enabled setting nobody can use into a refusal at startup rather than a page of 404s, and the question is asked of
/// the environment's own file provider so that nothing here reads a disk.
/// </remarks>
public sealed class ClientApplicationFilesTests
{
    private const int ClientListenerPort = 8443;

    private const int AnotherListenerPort = 8444;

    private const string EntryDocumentContent = "<!doctype html>";

    private const string ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";

    /// <summary>What the client's build actually writes, which names the directive a published origin joins.</summary>
    private const string ContentSecurityPolicyAdmittingTheDeployment =
        "default-src 'self'; connect-src 'self'; frame-ancestors 'none'";

    [Fact]
    public void BundleIsPresent_AnEnvironmentWhoseWebRootCarriesTheEntryDocumentAndItsPolicy_ReportsTheBundle()
    {
        // Arrange
        var environment = EnvironmentServing(entryDocumentPresent: true, contentSecurityPolicyPresent: true);

        // Act
        var present = ClientApplicationFiles.BundleIsPresent(environment);

        // Assert
        Assert.True(present);
    }

    /// <summary>The directory an image without a client still has, which is why its existence proves nothing.</summary>
    [Fact]
    public void BundleIsPresent_AnEnvironmentWhoseWebRootIsEmpty_ReportsNoBundle()
    {
        // Arrange
        var environment = EnvironmentServing(entryDocumentPresent: false, contentSecurityPolicyPresent: false);

        // Act
        var present = ClientApplicationFiles.BundleIsPresent(environment);

        // Assert
        Assert.False(present);
    }

    /// <summary>A bundle built without its policy would be served undefended, so it is not recognized as one.</summary>
    [Fact]
    public void BundleIsPresent_AWebRootCarryingNoContentSecurityPolicy_ReportsNoBundle()
    {
        // Arrange
        var environment = EnvironmentServing(entryDocumentPresent: true, contentSecurityPolicyPresent: false);

        // Act
        var present = ClientApplicationFiles.BundleIsPresent(environment);

        // Assert
        Assert.False(present);
    }

    [Fact]
    public async Task UseClientApplication_TheApplicationPathOnAClientListener_ServesTheEntryDocument()
    {
        // Arrange
        var context = RequestOnTheClientListener("/app/");

        // Act
        await ServeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(EntryDocumentContent, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    /// <summary>The bare path is redirected rather than answered, because the bundle's relative references resolve only beneath the trailing slash.</summary>
    [Fact]
    public async Task UseClientApplication_TheBareApplicationPathOnAClientListener_RedirectsToItsTrailingSlashForm()
    {
        // Arrange
        var context = RequestOnTheClientListener(ClientApplicationOptions.RequestPath);

        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mail.example.test");

        // Act
        await ServeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal(
            $"https://mail.example.test{ClientApplicationOptions.RequestPath}/",
            context.Response.Headers.Location.ToString());
    }

    /// <summary>The root belongs to no static file, so a request there passes on to whatever the pipeline serves next.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task UseClientApplication_APathOutsideTheApplicationPath_ServesNothing(string path)
    {
        // Arrange
        var context = RequestOnTheClientListener(path);

        // Act
        await ServeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task UseClientApplication_TheEntryDocumentOnAClientListener_CarriesTheBundlesContentSecurityPolicy()
    {
        // Arrange
        var context = RequestOnTheClientListener("/app/");

        // Act
        await ServeAsync(context);

        // Assert
        Assert.Equal(ContentSecurityPolicy, context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    /// <summary>The policy travels with the bundle, so a listener the bundle is not served on answers without either.</summary>
    [Fact]
    public async Task UseClientApplication_TheApplicationPathOnAnotherListener_CarriesNoContentSecurityPolicy()
    {
        // Arrange
        var context = RequestOn(AnotherListenerPort, "/app/");

        // Act
        await ServeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    /// <summary>The ordinary deployment's page stays under exactly the policy its build proved.</summary>
    [Fact]
    public async Task UseClientApplication_ADeploymentPublishingNoSignInMethod_ServesTheBundlesPolicyByteForByte()
    {
        // Arrange
        var context = RequestOnTheClientListener("/app/");

        // Act
        await ServeAsync(context, ContentSecurityPolicyAdmittingTheDeployment);

        // Assert
        Assert.Equal(ContentSecurityPolicyAdmittingTheDeployment, context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    /// <summary>
    /// The bundle's own <c>connect-src</c> is <c>'self'</c>, which refuses the discovery read and the token request on
    /// every deployment that publishes a server — and which servers those are is an operator's configuration rather
    /// than anything a build could know. The origins join the directive so the deployment the page talks to survives.
    /// </summary>
    [Fact]
    public async Task UseClientApplication_ADeploymentPublishingServers_AdmitsEachOriginBesideTheDeploymentItself()
    {
        // Arrange
        var context = RequestOnTheClientListener("/app/");

        // Act
        await ServeAsync(
            context,
            ContentSecurityPolicyAdmittingTheDeployment,
            "https://sso.example.test",
            "https://id.partner.example.test");

        // Assert
        Assert.Equal(
            "default-src 'self'; connect-src 'self' https://sso.example.test https://id.partner.example.test; frame-ancestors 'none'",
            context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    /// <summary>
    /// Appending the origins alone would leave the page under a directive that no longer falls back to
    /// <c>default-src</c>, and the client would stop being able to call its own deployment.
    /// </summary>
    [Fact]
    public async Task UseClientApplication_ABundleNamingNoConnectSource_AdmitsTheDeploymentBesideThePublishedOrigins()
    {
        // Arrange
        var context = RequestOnTheClientListener("/app/");

        // Act
        await ServeAsync(context, ContentSecurityPolicy, "https://sso.example.test");

        // Assert
        Assert.Equal(
            "default-src 'self'; frame-ancestors 'none'; connect-src 'self' https://sso.example.test",
            context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    /// <summary>An empty file passes a check for its existence, and serving under it would drop the header on every response.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public async Task UseClientApplication_ABundleWhosePolicyDocumentIsBlank_RefusesToServeIt(string policyDocument)
    {
        // Arrange
        await using var services = new ServiceCollection().BuildServiceProvider();

        var application = new ApplicationBuilder(services);

        // Act
        var refusal = Record.Exception(() => application.UseClientApplication(
            WebRootCarryingTheBundle(policyDocument),
            new HashSet<int> { ClientListenerPort },
            []));

        // Assert
        var refused = Assert.IsType<InvalidOperationException>(refusal);

        Assert.Contains(ClientApplicationOptions.ContentSecurityPolicyDocument, refused.Message, StringComparison.Ordinal);
    }

    private static DefaultHttpContext RequestOnTheClientListener(string path) => RequestOn(ClientListenerPort, path);

    private static DefaultHttpContext RequestOn(int localPort, string path)
    {
        var context = new DefaultHttpContext();

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Connection.LocalPort = localPort;
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static Task ServeAsync(DefaultHttpContext context) => ServeAsync(context, ContentSecurityPolicy);

    private static async Task ServeAsync(DefaultHttpContext context, string policy, params string[] signInOrigins)
    {
        // Written the way the client's build writes it, one line with a trailing newline the service must not attach.
        var environment = WebRootCarryingTheBundle($"{policy}\n");

        await using var services = new ServiceCollection()
            .AddSingleton(environment)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var application = new ApplicationBuilder(services);

        application.UseClientApplication(environment, new HashSet<int> { ClientListenerPort }, signInOrigins);
        application.Run(unmatched =>
        {
            unmatched.Response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        });

        context.RequestServices = services;

        await application.Build()(context);
    }

    /// <summary>A web root holding the entry document and a policy document at its root, which is where the image copies the bundle.</summary>
    private static IWebHostEnvironment WebRootCarryingTheBundle(string policyDocumentContent)
    {
        var content = Encoding.UTF8.GetBytes(EntryDocumentContent);

        var entryDocument = Substitute.For<IFileInfo>();

        entryDocument.Exists.Returns(true);
        entryDocument.Name.Returns(ClientApplicationOptions.EntryDocument);
        entryDocument.Length.Returns(content.Length);
        entryDocument.LastModified.Returns(DateTimeOffset.UnixEpoch);
        entryDocument.CreateReadStream().Returns(_ => new MemoryStream(content));

        var policy = Encoding.UTF8.GetBytes(policyDocumentContent);

        var policyDocument = Substitute.For<IFileInfo>();

        policyDocument.Exists.Returns(true);
        policyDocument.CreateReadStream().Returns(_ => new MemoryStream(policy));

        var absent = Substitute.For<IFileInfo>();

        absent.Exists.Returns(false);

        var root = Substitute.For<IDirectoryContents>();

        root.Exists.Returns(true);

        var absentDirectory = Substitute.For<IDirectoryContents>();

        absentDirectory.Exists.Returns(false);

        var files = Substitute.For<IFileProvider>();

        files.GetFileInfo(Arg.Any<string>()).Returns(absent);
        files.GetFileInfo($"/{ClientApplicationOptions.EntryDocument}").Returns(entryDocument);
        files.GetFileInfo(ClientApplicationOptions.ContentSecurityPolicyDocument).Returns(policyDocument);
        files.GetDirectoryContents(Arg.Any<string>()).Returns(absentDirectory);
        files.GetDirectoryContents("/").Returns(root);

        var environment = Substitute.For<IWebHostEnvironment>();

        environment.WebRootFileProvider.Returns(files);

        return environment;
    }

    private static IWebHostEnvironment EnvironmentServing(bool entryDocumentPresent, bool contentSecurityPolicyPresent)
    {
        var entryDocument = Substitute.For<IFileInfo>();

        entryDocument.Exists.Returns(entryDocumentPresent);

        var contentSecurityPolicy = Substitute.For<IFileInfo>();

        contentSecurityPolicy.Exists.Returns(contentSecurityPolicyPresent);

        var files = Substitute.For<IFileProvider>();

        files.GetFileInfo(ClientApplicationOptions.EntryDocument).Returns(entryDocument);
        files.GetFileInfo(ClientApplicationOptions.ContentSecurityPolicyDocument).Returns(contentSecurityPolicy);

        var environment = Substitute.For<IWebHostEnvironment>();

        environment.WebRootFileProvider.Returns(files);

        return environment;
    }
}
