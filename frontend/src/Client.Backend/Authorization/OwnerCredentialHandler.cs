// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Presents the signed-in owner's credential on every request that goes to the deployment.</summary>
/// <remarks>
/// <para>
/// In the pipeline rather than at each call site, so a route added later cannot forget it and a route added later
/// cannot present it to somewhere else: the handler is registered on the deployment's own transport, whose base
/// address <see cref="DeploymentAddress" /> decides, and nothing else in this assembly sends through it.
/// </para>
/// <para>
/// The header alone, never a query parameter. A credential in a query reaches every access log, proxy, and browser
/// history on the path — and this one is a password rather than a token that would have expired, so the exposure has
/// no end.
/// </para>
/// <para>
/// A request made before anybody signs in goes out unauthenticated rather than failing here. The session route answers
/// such a caller by design, and a deployment refusing the rest is the authoritative answer about what an absent
/// credential may do — one this client is in no position to anticipate.
/// </para>
/// </remarks>
internal sealed class OwnerCredentialHandler : DelegatingHandler
{
    private readonly SignedInOwner owner;

    /// <summary>Initializes the handler over the session the credential lives in.</summary>
    /// <param name="owner">Who is signed in during this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    public OwnerCredentialHandler(SignedInOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        this.owner = owner;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (this.owner.Current is { } credential)
        {
            request.Headers.Authorization = BasicCredentialHeader.ComposedFrom(credential);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
