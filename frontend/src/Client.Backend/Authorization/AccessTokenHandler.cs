// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Attaches the signed-in token to every request that goes to the deployment.</summary>
/// <remarks>
/// <para>
/// In the pipeline rather than at each call site, so a route added later cannot forget it and a route added later
/// cannot present it to somewhere else: the handler is registered on the deployment's own client, whose base address
/// the host stated, and nothing else in this assembly sends through it.
/// </para>
/// <para>
/// The header alone, never a query parameter. A credential in a query reaches every access log, proxy, and browser
/// history on the path, which is why the deployment publishes the header as the one bearer method it supports.
/// </para>
/// <para>
/// A request made before anybody signs in goes out unauthenticated rather than failing here. The session route answers
/// such a caller by design, and a deployment refusing the rest is the authoritative answer about what an absent
/// credential may do — one this client is in no position to anticipate.
/// </para>
/// </remarks>
internal sealed class AccessTokenHandler : DelegatingHandler
{
    private readonly AccessTokenStore tokens;

    /// <summary>Initializes the handler over the store the token lives in.</summary>
    /// <param name="tokens">Where the access token is held for this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tokens" /> is <see langword="null" />.</exception>
    public AccessTokenHandler(AccessTokenStore tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        this.tokens = tokens;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (this.tokens.Current is { Length: > 0 } accessToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
