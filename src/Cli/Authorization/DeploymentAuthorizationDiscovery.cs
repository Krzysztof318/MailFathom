// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.Common.OAuth;

namespace MailFathom.Cli.Authorization;

/// <summary>Finds out where to authorize, by asking the deployment and then the server it names.</summary>
/// <remarks>
/// <para>
/// Two documents and no configuration. The deployment publishes which authorization servers it accepts, the resource
/// identifier a token must be issued for, and the scopes it requires; the server publishes where a person approves a
/// sign-in and where a grant is exchanged. Everything the sign-in needs comes from one of the two, which is what keeps
/// <c>login</c> from being a command an operator has to prepare four values for, each wrong in its own way.
/// </para>
/// <para>
/// The issuer the deployment names is the anchor. A discovery document is accepted only when the <c>issuer</c> it
/// reports equals that one, as RFC 8414 section 3.3 requires, so a document served at a guessable address cannot move
/// the sign-in to a server the deployment never trusted — which would mean approving a MailFathom sign-in at somebody
/// else's login page.
/// </para>
/// </remarks>
internal sealed class DeploymentAuthorizationDiscovery
{
    private readonly HttpClient transport;

    /// <summary>Initializes discovery over a transport aimed at one deployment.</summary>
    /// <param name="transport">The transport, whose <see cref="HttpClient.BaseAddress" /> names the deployment.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport" /> is <see langword="null" />.</exception>
    internal DeploymentAuthorizationDiscovery(HttpClient transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        this.transport = transport;
    }

    /// <summary>Reads what one deployment and its authorization server publish about signing in.</summary>
    /// <param name="requestedIssuer">The issuer to sign in at when the deployment accepts several, or <see langword="null" /> to require that it accepts exactly one.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>Everything a grant needs.</returns>
    /// <exception cref="CliFailure">Thrown when either document is missing or unusable, or when the deployment accepts no server this sign-in could use.</exception>
    internal async Task<DeploymentAuthorization> ReadAsync(string? requestedIssuer, CancellationToken cancellationToken)
    {
        var resourceMetadata = await this.ReadProtectedResourceMetadataAsync(cancellationToken);

        var issuer = SelectIssuer(resourceMetadata, requestedIssuer);

        if (resourceMetadata.Resource is not { Length: > 0 } resource)
        {
            throw new CliFailure(
                "The deployment's metadata document names no resource, so no token could be requested for it. That is a defect in the deployment rather than in this sign-in.");
        }

        var serverMetadata = await this.ReadAuthorizationServerMetadataAsync(issuer, cancellationToken);

        if (ReadEndpoint(serverMetadata.TokenEndpoint) is not { } tokenEndpoint)
        {
            throw new CliFailure(
                $"The authorization server at {issuer} publishes no https token endpoint, so no grant could be exchanged there.");
        }

        return new DeploymentAuthorization(
            issuer,
            ReadEndpoint(serverMetadata.AuthorizationEndpoint),
            tokenEndpoint,
            ReadEndpoint(serverMetadata.DeviceAuthorizationEndpoint),
            resource,
            string.Join(' ', resourceMetadata.ScopesSupported ?? []));
    }

    /// <summary>Names the one authorization server this sign-in will use.</summary>
    /// <remarks>
    /// A deployment may accept several, and which of them an operator belongs to is not something the command can work
    /// out: they are separate populations, and taking the first would sign an operator in at whichever the deployment
    /// happened to list first. Several without <c>--issuer</c> is therefore refused, with the choices named.
    /// </remarks>
    private static string SelectIssuer(ProtectedResourceMetadata resourceMetadata, string? requestedIssuer)
    {
        var issuers = resourceMetadata.AuthorizationServers ?? [];

        if (issuers.Count == 0)
        {
            throw new CliFailure(
                "The deployment names no authorization server, so there is nowhere to sign in. Present an API key instead, or ask the operator to configure one.");
        }

        if (requestedIssuer is not { Length: > 0 })
        {
            return issuers.Count == 1
                ? issuers[0]
                : throw new CliFailure(
                    $"The deployment accepts tokens from several authorization servers, so name the one to sign in at with --issuer. It accepts: {string.Join(", ", issuers)}.");
        }

        return issuers.FirstOrDefault(issuer => string.Equals(issuer, requestedIssuer, StringComparison.Ordinal))
            ?? throw new CliFailure(
                $"The deployment does not accept tokens from '{requestedIssuer}'. It accepts: {string.Join(", ", issuers)}.");
    }

    /// <summary>Reads an endpoint the authorization server published.</summary>
    /// <remarks>
    /// Anything that is not an absolute <c>https</c> address is read as absent. A token endpoint reached over plain HTTP
    /// would carry the authorization code, the proof key, and the issued tokens in clear, and an endpoint the command
    /// cannot use is better reported as one the server does not publish than followed anyway.
    /// </remarks>
    private static Uri? ReadEndpoint(string? publishedEndpoint) =>
        Uri.TryCreate(publishedEndpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
            ? endpoint
            : null;

    private async Task<ProtectedResourceMetadata> ReadProtectedResourceMetadataAsync(CancellationToken cancellationToken)
    {
        using var response = await this.SendAsync(
            AdminEndpointRoutes.ProtectedResourceMetadataPath,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new CliFailure(
                "The deployment publishes no OAuth metadata, so it accepts no access token. Sign in with an API key instead, or ask the operator to add 'OAuth' to the endpoint's authentication methods.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliFailure($"The deployment answered {(int)response.StatusCode} rather than its OAuth metadata.");
        }

        const string NotAMetadataDocument =
            "The address answered, but not with an OAuth metadata document. Check that it is the administrative endpoint rather than another service on the same host.";

        try
        {
            return await response.Content.ReadFromJsonAsync(
                    CliJsonContext.Default.ProtectedResourceMetadata,
                    cancellationToken)
                ?? throw new CliFailure(NotAMetadataDocument);
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            throw new CliFailure(NotAMetadataDocument, failure);
        }
    }

    /// <summary>Reads the issuer's discovery document from wherever it publishes one.</summary>
    /// <remarks>
    /// The candidate addresses and their order are the MCP authorization specification's, shared with the resource
    /// server rather than restated here: a server may publish OAuth 2.0 Authorization Server Metadata, OpenID Connect
    /// Discovery, or both, at addresses that differ once the issuer has a path. The first that answers with a document
    /// reporting this issuer wins, and every candidate is derived from the issuer, so the search reaches only the server
    /// the deployment already named.
    /// </remarks>
    private async Task<AuthorizationServerMetadata> ReadAuthorizationServerMetadataAsync(
        string issuer,
        CancellationToken cancellationToken)
    {
        if (!OAuthIdentifierUri.IsWellFormed(issuer))
        {
            throw new CliFailure(
                $"The deployment named '{issuer}' as an authorization server, and that is not an issuer identifier. That is a defect in the deployment rather than in this sign-in.");
        }

        foreach (var candidateAddress in OAuthMetadataAddresses.ForIssuer(issuer))
        {
            var metadata = await this.TryReadAuthorizationServerMetadataAsync(candidateAddress, issuer, cancellationToken);

            if (metadata is not null)
            {
                return metadata;
            }
        }

        throw new CliFailure(
            $"No discovery document reporting '{issuer}' was found, so where to sign in could not be established. Check that the authorization server is reachable from this machine.");
    }

    /// <summary>Tries one candidate address, reporting a miss rather than failing the search.</summary>
    /// <remarks>
    /// An address the server does not publish at answers <c>404</c>, answers something that is not a document, or fails
    /// to connect. All of them mean "not here", so the next candidate is tried and the search fails only once every
    /// candidate has. A candidate that hangs until this client's own timeout is one of them, which is why the caller's
    /// token decides rather than the exception's type: treating a client timeout as cancellation would let one
    /// unresponsive address hide the document published at the next.
    /// </remarks>
    private async Task<AuthorizationServerMetadata?> TryReadAuthorizationServerMetadataAsync(
        string candidateAddress,
        string issuer,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, candidateAddress);

        try
        {
            using var response = await this.transport.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var metadata = await response.Content.ReadFromJsonAsync(
                CliJsonContext.Default.AuthorizationServerMetadata,
                cancellationToken);

            return string.Equals(metadata?.Issuer, issuer, StringComparison.Ordinal) ? metadata : null;
        }
        catch (Exception failure) when (
            failure is JsonException or NotSupportedException or HttpRequestException
            || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>Fetches one document, turning a transport failure into something an operator can act on.</summary>
    private async Task<HttpResponseMessage> SendAsync(string address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);

        try
        {
            return await this.transport.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliFailure($"The deployment at {this.transport.BaseAddress} did not answer in time.");
        }
        catch (HttpRequestException failure)
        {
            throw new CliFailure(
                $"The deployment at {this.transport.BaseAddress} could not be reached: {failure.Message}",
                failure);
        }
    }
}
