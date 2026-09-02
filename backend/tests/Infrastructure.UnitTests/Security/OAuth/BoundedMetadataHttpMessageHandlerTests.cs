// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.OAuth;

/// <summary>Covers the bounds on the one thing this process fetches from a machine it does not own.</summary>
/// <remarks>
/// A key refresh happens inside a request's authentication path and on a schedule nobody watches, so an authorization
/// server that has been replaced or taken over is in a position to answer it with something other than a few kilobytes
/// of JSON. Every refusal below is an ordinary retrieval failure, which the caller already treats as "the metadata could
/// not be read" rather than as a reason to accept a token.
/// </remarks>
public sealed class BoundedMetadataHttpMessageHandlerTests
{
    private const int SizeLimit = 1024;

    private static readonly Uri DiscoveryDocument = new("https://sso.example.test/.well-known/openid-configuration");

    [Fact]
    public async Task SendAsync_AMetadataDocumentInsideTheLimit_IsRead()
    {
        // Arrange
        using var response = Response(new StringContent(new string('a', 512)));
        using var transport = TransportAnswering(response);
        using var handler = new BoundedMetadataHttpMessageHandler(SizeLimit) { InnerHandler = transport };
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act
        using var retrieved = await client.GetAsync(DiscoveryDocument, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
    }

    /// <summary>A declared length beyond the limit is refused before the body is read at all.</summary>
    [Fact]
    public async Task SendAsync_AResponseDeclaringMoreThanTheLimit_IsRefused()
    {
        // Arrange
        using var response = Response(new StringContent(new string('a', SizeLimit + 1)));
        using var transport = TransportAnswering(response);
        using var handler = new BoundedMetadataHttpMessageHandler(SizeLimit) { InnerHandler = transport };
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act, Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(DiscoveryDocument, TestContext.Current.CancellationToken));
    }

    /// <summary>A server that does not say how much it intends to send is the case the limit exists for, so the body is buffered against it rather than trusted.</summary>
    [Fact]
    public async Task SendAsync_AResponseWithNoDeclaredLengthBeyondTheLimit_IsRefused()
    {
        // Arrange
        var oversizedBody = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', SizeLimit + 1)));
        using var response = Response(new StreamContent(oversizedBody));
        using var transport = TransportAnswering(response);
        using var handler = new BoundedMetadataHttpMessageHandler(SizeLimit) { InnerHandler = transport };
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act, Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(DiscoveryDocument, TestContext.Current.CancellationToken));
    }

    /// <summary>Metadata is retrieved over https only, whatever a configuration or a redirect might have produced.</summary>
    [Fact]
    public async Task SendAsync_APlaintextRetrieval_IsRefusedWithoutBeingSent()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("The request must never reach the transport."));
        using var handler = new BoundedMetadataHttpMessageHandler(SizeLimit) { InnerHandler = transport };
        using var client = new HttpClient(handler, disposeHandler: false);

        // Act, Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(
            new Uri("http://sso.example.test/.well-known/openid-configuration"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_ANonPositiveLimit_IsRefused() =>

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedMetadataHttpMessageHandler(0));

    private static HttpResponseMessage Response(HttpContent body) => new(HttpStatusCode.OK) { Content = body };

    private static FakeHttpMessageHandler TransportAnswering(HttpResponseMessage response) =>
        new((_, _) => Task.FromResult(response));
}
