// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Transport;

namespace MailFathom.Cli.Administration;

/// <summary>Produces the profile a command acts through, with a credential the deployment will still accept.</summary>
/// <remarks>
/// <para>
/// Every command that reaches a deployment goes through here rather than reading the store directly, because an OAuth
/// access token is spent within the hour and renewing it is not something each command should remember to do. Doing it
/// in one place is what makes that lifetime something the operator never experiences: the renewal happens, the new
/// token is written back, and the command carries on.
/// </para>
/// <para>
/// An API key profile passes through untouched. Its credential has no expiry the command knows about — the deployment
/// decides when a key stops working, and asks nothing of the command in the meantime.
/// </para>
/// <para>
/// A key-pair profile is the opposite case and lands on the same seam: it stores no credential at all, so one is minted
/// here from the private key it names, spent on this command's request, and forgotten. Doing it here rather than in each
/// command is what keeps the difference invisible above this point — every command holds a profile with a usable token,
/// whichever of the three ways it came by one.
/// </para>
/// </remarks>
internal sealed class DeploymentAccess
{
    private readonly CredentialStore store;
    private readonly Func<Uri, StoredTransportTrust, DeploymentTransport> openTransport;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes access over the store, the transport, and the clock a command was given.</summary>
    /// <param name="store">Where the profiles live.</param>
    /// <param name="openTransport">Opens a transport aimed at one address; this type disposes what it opens.</param>
    /// <param name="timeProvider">Decides whether the stored access token is still usable.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DeploymentAccess(
        CredentialStore store,
        Func<Uri, StoredTransportTrust, DeploymentTransport> openTransport,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(openTransport);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.openTransport = openTransport;
        this.timeProvider = timeProvider;
    }

    /// <summary>Settles which deployment a command acts on, renewing its access token when the stored one is spent.</summary>
    /// <param name="requestedDeployment">A profile name, an absolute address, or <see langword="null" /> to use the default.</param>
    /// <param name="cancellationToken">Cancels a renewal.</param>
    /// <returns>The profile, carrying a credential that has not expired.</returns>
    /// <exception cref="CliFailure">Thrown when the operator is not signed in to what they named, when the sign-in has ended, or when a key-pair profile's private key is unreadable.</exception>
    internal async Task<SignedInProfile> ReachAsync(string? requestedDeployment, CancellationToken cancellationToken)
    {
        var profile = this.store.Resolve(requestedDeployment);

        if (profile.KeyPair is { } keyPair)
        {
            return profile with
            {
                Token = ClientAssertionCredential.MintFor(
                    keyPair.PrivateKeyPath,
                    this.timeProvider.GetUtcNow()),
            };
        }

        return profile.Session is { } session && this.IsSpent(session)
            ? await this.RenewAsync(profile, session, cancellationToken)
            : profile;
    }

    /// <summary>Reports whether the stored access token is close enough to expiry to be treated as spent.</summary>
    /// <remarks>The skew is what stops a token from expiring while the request carrying it is in flight, which the operator would otherwise read as a sign-in that failed for no reason.</remarks>
    private bool IsSpent(OAuthSession session) =>
        this.timeProvider.GetUtcNow() >= session.AccessTokenExpiresAt - DeploymentAuthorizer.RenewalSkew;

    private async Task<SignedInProfile> RenewAsync(
        SignedInProfile profile,
        OAuthSession session,
        CancellationToken cancellationToken)
    {
        // The authorization server rather than the deployment, so the profile's own certificate pin does not travel
        // here: it names the deployment's certificate and would refuse every certificate this host could present.
        using var transport = this.openTransport(session.TokenEndpoint, StoredTransportTrust.Protected);

        var authorization = new DeploymentAuthorization(
            session.Issuer,
            AuthorizationEndpoint: null,
            session.TokenEndpoint,
            DeviceAuthorizationEndpoint: null,
            session.Resource,
            session.Scope);

        var renewed = await new DeploymentAuthorizer(transport.Client, this.timeProvider)
            .RefreshAsync(authorization, session.ClientId, session.RefreshToken, cancellationToken);

        this.store.RenewAccessToken(profile.Name, renewed.AccessToken, renewed.AccessTokenExpiresAt);

        return profile with
        {
            Token = renewed.AccessToken,
            Session = session with { AccessTokenExpiresAt = renewed.AccessTokenExpiresAt },
        };
    }
}
