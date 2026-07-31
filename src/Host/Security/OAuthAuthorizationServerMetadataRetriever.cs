// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MailMcp.Host.Security;

/// <summary>Finds one authorization server's discovery document across the addresses the MCP specification names.</summary>
/// <remarks>
/// <para>
/// The framework's own retriever reads one address, which is enough when a deployment knows which specification its
/// authorization server follows. The MCP authorization specification does not assume that: a server may publish OAuth
/// 2.0 Authorization Server Metadata, OpenID Connect Discovery, or both, at addresses that differ once the issuer has a
/// path. This retriever therefore tries them in the order that specification states and takes the first that answers.
/// </para>
/// <para>
/// A document is only accepted when the <c>issuer</c> it reports equals the issuer this profile was configured with.
/// RFC 8414 section 3.3 requires that equality, and it is what stops a document served at a discoverable address from
/// naming an issuer the operator never trusted — which would otherwise hand the choice of signing keys to whoever
/// controls that address.
/// </para>
/// <para>
/// Nothing here reads an endpoint by convention. The authorization, token, registration, and key set addresses all come
/// out of the document, so a server that moves one of them keeps working and a server that publishes none of them fails
/// to be configured rather than being reached at a guessed path.
/// </para>
/// </remarks>
internal sealed class OAuthAuthorizationServerMetadataRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly string authorizationServerName;
    private readonly string expectedIssuer;
    private readonly IReadOnlyList<string> candidateAddresses;

    /// <summary>Initializes a new retriever for one configured authorization server.</summary>
    /// <param name="authorizationServerName">The operator's name for the profile, which a failure names.</param>
    /// <param name="expectedIssuer">The issuer the document must report.</param>
    /// <param name="candidateAddresses">The addresses to try, in order.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="candidateAddresses" /> is empty.</exception>
    internal OAuthAuthorizationServerMetadataRetriever(
        string authorizationServerName,
        string expectedIssuer,
        IReadOnlyList<string> candidateAddresses)
    {
        ArgumentNullException.ThrowIfNull(authorizationServerName);
        ArgumentNullException.ThrowIfNull(expectedIssuer);
        ArgumentNullException.ThrowIfNull(candidateAddresses);

        if (candidateAddresses.Count == 0)
        {
            throw new ArgumentException(
                "An authorization server profile must name at least one address to look for its metadata at.",
                nameof(candidateAddresses));
        }

        this.authorizationServerName = authorizationServerName;
        this.expectedIssuer = expectedIssuer;
        this.candidateAddresses = candidateAddresses;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The address the configuration manager passes is ignored, because this profile's addresses are several and were
    /// decided when it was composed. The manager still carries the first of them, which is what its own diagnostics name.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no candidate address answered with a document reporting the configured issuer.</exception>
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        foreach (var candidateAddress in this.candidateAddresses)
        {
            var configuration = await this.TryReadAsync(candidateAddress, retriever, cancel);

            if (configuration is not null)
            {
                return configuration;
            }
        }

        // Neither the addresses nor the issuer appear in the message. Both name the operator's identity provider, and an
        // exception message travels further than the configuration that produced it; the profile name is MailMcp's own
        // and is enough to say which section to look at.
        throw new InvalidOperationException(
            $"No discovery document reporting the configured issuer was found for authorization server '{this.authorizationServerName}'.");
    }

    private async Task<OpenIdConnectConfiguration?> TryReadAsync(
        string candidateAddress,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        try
        {
            var configuration = await OpenIdConnectConfigurationRetriever.GetAsync(candidateAddress, retriever, cancel);

            return string.Equals(configuration.Issuer, this.expectedIssuer, StringComparison.Ordinal)
                ? configuration
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            // An address that is not the one this server publishes at answers 404, answers something that is not a
            // document, or fails to connect. All of them mean "not here" and the next candidate is tried; the retrieval
            // as a whole only fails once every candidate has.
            //
            // A candidate that hangs until the client's own timeout is one of them, which is why the caller's token is
            // what the filter asks about rather than the exception's type: an HttpClient timeout arrives as a
            // TaskCanceledException carrying a token nobody here cancelled, and treating that as caller cancellation
            // would let one unresponsive address hide the document published at the next one.
            return null;
        }
    }
}
