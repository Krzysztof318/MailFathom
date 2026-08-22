// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Finds out where to sign in, by asking the deployment and then the server it names.</summary>
/// <remarks>
/// <para>
/// Two documents and no configuration. The deployment publishes which authorization servers it accepts, the resource
/// identifier a token must be issued for, and the scopes a client should ask for; the server publishes where a person
/// approves a sign-in and where the code is exchanged. Everything the flow needs comes from one of the two, which is
/// what keeps the client from being something somebody has to configure four values for, each wrong in its own way.
/// </para>
/// <para>
/// The issuer the deployment names is the anchor. A discovery document is accepted only when the <c>issuer</c> it
/// reports equals that one, as RFC 8414 section 3.3 requires, so a document served at a guessable address cannot move
/// the sign-in to a server the deployment never trusted — which would mean approving a MailFathom sign-in at somebody
/// else's login page.
/// </para>
/// <para>
/// The scope list is taken verbatim. A client that appends a scope of its own asks an authorization server for
/// something the deployment never published, and what a session is allowed to be is the deployment's decision to state
/// rather than this client's to assume.
/// </para>
/// </remarks>
internal sealed class DeploymentAuthorizationDiscovery
{
    private readonly HttpClient deployment;
    private readonly HttpClient authorizationServer;

    /// <summary>Initializes discovery over the two transports the two documents come from.</summary>
    /// <param name="deployment">Aimed at the deployment, whose base address the host stated.</param>
    /// <param name="authorizationServer">Aimed at nothing in particular; every address it is given is absolute and derived from the issuer the deployment named.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Two transports because the two documents come from two machines. The deployment's carries whatever the host
    /// configured about the deployment, and applying that to somebody's identity provider would be aiming one machine's
    /// settings at another's.
    /// </remarks>
    internal DeploymentAuthorizationDiscovery(HttpClient deployment, HttpClient authorizationServer)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(authorizationServer);

        this.deployment = deployment;
        this.authorizationServer = authorizationServer;
    }

    /// <summary>Reads what one deployment and its authorization server publish about signing in.</summary>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>Everything the grant needs.</returns>
    /// <exception cref="DeploymentFailure">Thrown when either document is missing or unusable, or the deployment accepts no server this client could use.</exception>
    internal async Task<DeploymentAuthorization> ReadAsync(CancellationToken cancellationToken)
    {
        var resourceMetadata = await this.ReadProtectedResourceMetadataAsync(cancellationToken).ConfigureAwait(false);

        var issuer = SelectIssuer(resourceMetadata);

        if (resourceMetadata.Resource is not { Length: > 0 } resource)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom names no resource identifier for itself, so no token could be requested for it.");
        }

        var serverMetadata = await this.ReadAuthorizationServerMetadataAsync(issuer, cancellationToken).ConfigureAwait(false);

        if (ReadEndpoint(serverMetadata.AuthorizationEndpoint) is not { } authorizationEndpoint)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The authorization server publishes no https authorization endpoint, so there is nowhere to approve the sign-in.");
        }

        if (ReadEndpoint(serverMetadata.TokenEndpoint) is not { } tokenEndpoint)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The authorization server publishes no https token endpoint, so no grant could be exchanged there.");
        }

        return new DeploymentAuthorization(
            issuer,
            authorizationEndpoint,
            tokenEndpoint,
            resource,
            ReadPublishedScopes(resourceMetadata.ScopesSupported));
    }

    /// <summary>Names the one authorization server this sign-in will use.</summary>
    /// <remarks>
    /// A deployment accepting several is refused rather than resolved by taking the first. They are separate
    /// populations, and which of them somebody belongs to is not something an application with no screen for the
    /// question can work out — a client that guessed would send a person to approve a sign-in at an organization they
    /// have no account with.
    /// </remarks>
    private static string SelectIssuer(ProtectedResourceMetadata resourceMetadata)
    {
        var issuers = (resourceMetadata.AuthorizationServers ?? [])
            .Where(issuer => !string.IsNullOrWhiteSpace(issuer))
            .ToList();

        return issuers.Count switch
        {
            0 => throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom names no authorization server, so there is nowhere to sign in. Ask whoever runs it to configure one."),
            1 => issuers[0],
            _ => throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom accepts tokens from several authorization servers, and this application cannot choose between them."),
        };
    }

    /// <summary>Reads the scope list the deployment published, in the space-separated form RFC 6749 sends it in.</summary>
    /// <remarks>
    /// Blank entries are dropped rather than joined, because this document comes from a machine the process does not own
    /// and a list of them would compose into a scope parameter made of spaces — which is the empty parameter an absent
    /// one is deliberately not, and which several authorization servers refuse.
    /// </remarks>
    private static string ReadPublishedScopes(IReadOnlyList<string>? publishedScopes) =>
        string.Join(' ', (publishedScopes ?? []).Where(scope => !string.IsNullOrWhiteSpace(scope)));

    /// <summary>Reads an endpoint the authorization server published.</summary>
    /// <remarks>
    /// Anything that is not an absolute <c>https</c> address is read as absent. A token endpoint reached over plain HTTP
    /// would carry the proof key and the issued token in clear, and an endpoint this client cannot use is better
    /// reported as one the server does not publish than followed anyway.
    /// </remarks>
    private static Uri? ReadEndpoint(string? publishedEndpoint) =>
        Uri.TryCreate(publishedEndpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
            ? endpoint
            : null;

    private async Task<ProtectedResourceMetadata> ReadProtectedResourceMetadataAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DeploymentRoutes.ProtectedResourceMetadataPath);

        using var response = await DeploymentExchange
            .SendAsync(this.deployment, request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom publishes no OAuth metadata, so it accepts no access token. Ask whoever runs it to enable OAuth on the client endpoint.");
        }

        DeploymentExchange.RefuseUnusableStatus(response);

        return await DeploymentExchange
            .ReadBodyAsync(response, DeploymentJsonContext.Default.ProtectedResourceMetadata, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the issuer's discovery document from wherever it publishes one.</summary>
    /// <remarks>
    /// The candidate addresses and their order are the MCP authorization specification's: a server may publish OAuth
    /// 2.0 Authorization Server Metadata, OpenID Connect Discovery, or both, at addresses that differ once the issuer
    /// has a path. The first that answers with a document reporting this issuer wins, and every candidate is derived
    /// from that issuer, so the search reaches only the server the deployment already named.
    /// </remarks>
    private async Task<AuthorizationServerMetadata> ReadAuthorizationServerMetadataAsync(
        string issuer,
        CancellationToken cancellationToken)
    {
        var candidates = OAuthMetadataAddresses.ForIssuer(issuer);

        if (candidates.Count == 0)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom named something that is not an issuer identifier as its authorization server.");
        }

        foreach (var candidateAddress in candidates)
        {
            var metadata = await this
                .TryReadAuthorizationServerMetadataAsync(candidateAddress, issuer, cancellationToken)
                .ConfigureAwait(false);

            if (metadata is not null)
            {
                return metadata;
            }
        }

        throw new DeploymentFailure(
            DeploymentFailureReason.Unreachable,
            "The authorization server MailFathom named could not be reached, or publishes no discovery document.");
    }

    /// <summary>Tries one candidate address, reporting a miss rather than failing the search.</summary>
    /// <remarks>
    /// An address the server does not publish at answers <c>404</c>, answers something that is not a document, or fails
    /// to connect. All of them mean "not here", so the next candidate is tried and the search fails only once every
    /// candidate has. A candidate that hangs until this client's own timeout is one of them, which is why the caller's
    /// token decides rather than the failure's reason: treating a client timeout as cancellation would let one
    /// unresponsive address hide the document published at the next.
    /// </remarks>
    private async Task<AuthorizationServerMetadata?> TryReadAuthorizationServerMetadataAsync(
        string candidateAddress,
        string issuer,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(candidateAddress, UriKind.Absolute, out var address))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);

            using var response = await DeploymentExchange
                .SendAsync(this.authorizationServer, request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var metadata = await DeploymentExchange
                .ReadBodyAsync(response, DeploymentJsonContext.Default.AuthorizationServerMetadata, cancellationToken)
                .ConfigureAwait(false);

            return string.Equals(metadata.Issuer, issuer, StringComparison.Ordinal) ? metadata : null;
        }
        catch (DeploymentFailure) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
