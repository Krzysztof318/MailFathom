// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security.OAuth;

/// <summary>Keeps the retrieval of an authorization server's metadata inside stated bounds.</summary>
/// <remarks>
/// <para>
/// Discovery documents and key sets are the one thing this process fetches from a machine it does not own, and it
/// fetches them on a schedule nobody watches. A server that has been replaced, misconfigured, or taken over is therefore
/// in a position to answer a key refresh with something other than a few kilobytes of JSON, and the refresh happens
/// inside a request's authentication path.
/// </para>
/// <para>
/// Two bounds are enforced here and two are set where the client is composed. This handler refuses a response larger
/// than the limit and a request that is not <c>https</c>; the client that owns it carries the timeout and follows no
/// redirect, so a server cannot answer slowly forever and cannot send the retrieval somewhere the configuration never
/// named.
/// </para>
/// <para>
/// A declared length over the limit is refused before the body is read at all, and a body without one is read through a
/// buffer that stops at the same limit. Both fail as an ordinary retrieval failure, which the caller already treats as
/// "the metadata could not be read" rather than as a reason to accept a token.
/// </para>
/// </remarks>
public sealed class BoundedMetadataHttpMessageHandler : DelegatingHandler
{
    private readonly int sizeLimitInBytes;

    /// <summary>Initializes a new bounded metadata handler.</summary>
    /// <param name="sizeLimitInBytes">The largest response body accepted.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="sizeLimitInBytes" /> is not positive.</exception>
    /// <remarks>
    /// The handler that performs the request is not taken here. Both clients that carry this one are built by
    /// <see cref="IHttpClientFactory" />, which composes the chain and assigns <see cref="DelegatingHandler.InnerHandler" />
    /// itself, so a constructor taking an inner handler would be one no caller could use.
    /// </remarks>
    public BoundedMetadataHttpMessageHandler(int sizeLimitInBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeLimitInBytes);

        this.sizeLimitInBytes = sizeLimitInBytes;
    }

    /// <inheritdoc />
    /// <exception cref="HttpRequestException">Thrown when the request is not <c>https</c> or the response exceeds the configured size limit.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpRequestException(
                "Authorization server metadata is retrieved over https only, and this retrieval was not.");
        }

        var response = await base.SendAsync(request, cancellationToken);

        try
        {
            await this.EnsureWithinSizeLimitAsync(response.Content, cancellationToken);
        }
        catch
        {
            response.Dispose();

            throw;
        }

        return response;
    }

    private async Task EnsureWithinSizeLimitAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers is { ContentLength: { } declaredLength } && declaredLength > this.sizeLimitInBytes)
        {
            throw new HttpRequestException(
                $"The authorization server metadata declared {declaredLength} bytes, beyond the {this.sizeLimitInBytes} this host reads.");
        }

        // A response with no declared length is buffered rather than trusted, because the whole point of the limit is a
        // server that does not say how much it intends to send.
        await content.LoadIntoBufferAsync(this.sizeLimitInBytes, cancellationToken);
    }
}
