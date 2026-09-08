// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Signals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers what the client endpoint compresses, in which encoding, and what it leaves alone.</summary>
/// <remarks>
/// Asserted through the composed middleware rather than through the options it was registered with, because what an
/// operator is promised is a response that arrives compressed: an encoding the framework negotiates away, a media type
/// that turns out not to be in the compressed set, or a scheme the middleware refuses to compress over would each
/// leave every option correct and every response the size it was.
/// <para>
/// The bodies below repeat themselves the way the surface's own do — a mail list is the same field names around a
/// hundred rows — so that a body that was compressed is recognizable as one by its length rather than by the header
/// alone.
/// </para>
/// </remarks>
public sealed class ClientResponseCompressionTests
{
    private const string TimelinePath = ClientEndpointOptions.RoutePrefix
        + ClientMailTimelineEndpoint.MailTimelineRoute;

    private static readonly byte[] TimelinePage = Encoding.UTF8.GetBytes(
        $$"""{"emails":[{{string.Join(
            ",",
            Enumerable.Range(0, 100).Select(static row =>
                $$"""{"id":"{{row}}","subject":"Quarterly report","preview":"Please find the figures attached.","unread":false}"""))}}],"pageSize":100}""");

    /// <summary>What a mail list costs a reader on a request that offered an encoding, which is the whole of this issue.</summary>
    /// <remarks>
    /// The <c>Vary</c> header is asserted here rather than beside the uncompressed case because that is where it
    /// decides something: a cache that kept this body without it would hand a Brotli page to the next caller, whatever
    /// that caller offered.
    /// </remarks>
    [Fact]
    public async Task Response_ATimelinePageForARequestOfferingBrotli_IsServedCompressed()
    {
        // Act
        var served = await ServeAsync(TimelinePath, "application/json; charset=utf-8", "br, gzip");

        // Assert
        Assert.Equal("br", served.Encoding);
        Assert.True(
            served.Length < TimelinePage.Length,
            $"the page travelled as {served.Length} bytes of an uncompressed {TimelinePage.Length}");
        Assert.Contains("Accept-Encoding", served.Vary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A caller that cannot read Brotli is served what it can read rather than what this deployment prefers.</summary>
    [Fact]
    public async Task Response_ARequestOfferingOnlyGzip_IsServedGzipRatherThanBrotli()
    {
        // Act
        var served = await ServeAsync(TimelinePath, "application/json; charset=utf-8", "gzip");

        // Assert
        Assert.Equal("gzip", served.Encoding);
        Assert.True(served.Length < TimelinePage.Length);
    }

    /// <summary>A caller that offered nothing is answered with the bytes it asked for.</summary>
    [Fact]
    public async Task Response_ARequestOfferingNoEncoding_IsServedUnchanged()
    {
        // Act
        var served = await ServeAsync(TimelinePath, "application/json; charset=utf-8", acceptEncoding: null);

        // Assert
        Assert.Null(served.Encoding);
        Assert.Equal(TimelinePage.Length, served.Length);
    }

    /// <summary>An attachment is octets somebody else already compressed, and a second pass over them costs latency to make them longer.</summary>
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("application/zip")]
    [InlineData("application/problem+json")]
    public async Task Response_AMediaTypeOutsideTheCompressedSet_IsServedUnchanged(string contentType)
    {
        // Act
        var served = await ServeAsync(ClientEndpointOptions.RoutePrefix + "/emails/1/attachments/2", contentType, "br, gzip");

        // Assert
        Assert.Null(served.Encoding);
        Assert.Equal(TimelinePage.Length, served.Length);
    }

    /// <summary>The live channel is a connection rather than a response, so nothing buffers its writes to compress them — and the route that mints its tickets is under the same prefix.</summary>
    [Theory]
    [InlineData(ClientSignalEndpoints.HubPath)]
    [InlineData(ClientEndpointOptions.RoutePrefix + ClientSignalEndpoints.TicketRoute)]
    public async Task Response_TheSignalChannel_IsServedUncompressed(string path)
    {
        // Act
        var served = await ServeAsync(path, "text/plain", "br, gzip");

        // Assert
        Assert.Null(served.Encoding);
    }

    /// <summary>The other two surfaces are a separate reading, and a path that merely begins with the same letters is not this one.</summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/api/admin/accounts")]
    [InlineData("/api/clients")]
    [InlineData("/health/ready")]
    public async Task Response_APathThisSurfaceDoesNotServe_IsServedUncompressed(string path)
    {
        // Act
        var served = await ServeAsync(path, "application/json; charset=utf-8", "br, gzip");

        // Assert
        Assert.Null(served.Encoding);
    }

    /// <summary>Serves one body through the composed pipeline and reports what reached the wire.</summary>
    private static async Task<ServedResponse> ServeAsync(string path, string contentType, string? acceptEncoding)
    {
        // Logging is the one registration the framework's own middleware asks for and the composition under test does
        // not make: a host has it before any surface is composed.
        var services = new ServiceCollection()
            .AddLogging()
            .AddClientResponseCompression()
            .BuildServiceProvider();

        var pipeline = new ApplicationBuilder(services)
            .UseClientResponseCompression()
            .Use(_ => async context =>
            {
                context.Response.ContentType = contentType;

                await context.Response.Body.WriteAsync(TimelinePage);
            })
            .Build();

        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };

        // The scheme the surface is served over where a browser reaches it, which is what the registration turns
        // compression on for and therefore the one this has to assert under.
        context.Request.Scheme = "https";
        context.Request.Path = path;
        context.Response.Body = body;

        if (acceptEncoding is not null)
        {
            context.Request.Headers.AcceptEncoding = acceptEncoding;
        }

        await pipeline(context);

        return new ServedResponse(
            context.Response.Headers.ContentEncoding.ToString() is { Length: > 0 } encoding ? encoding : null,
            context.Response.Headers.Vary.ToString(),
            body.Length);
    }

    /// <summary>What one response put on the wire.</summary>
    /// <param name="Encoding">The encoding it was served under, or <see langword="null" /> where it carried none.</param>
    /// <param name="Vary">What the response says its shape depends on.</param>
    /// <param name="Length">The octets that travelled.</param>
    private sealed record ServedResponse(string? Encoding, string Vary, long Length);
}
