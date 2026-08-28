// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>What the deployment made of a credential offered to it.</summary>
/// <param name="Result">Whether it was accepted, and where it was not, which of the two refusals it was.</param>
/// <param name="Persistence">What became of keeping it, which is meaningful only where it was accepted.</param>
public sealed record SignInAttempt(SignInResult Result, CredentialPersistence Persistence);

/// <summary>What a deployment answered a credential with.</summary>
/// <remarks>
/// Three cases rather than two, because a deployment whose operator never enabled password sign-in is not a wrong
/// password and telling somebody it is sends them to change a password that was never the problem. Everything that is
/// not an answer — nothing there, nothing in time, something that is not MailFathom — is a
/// <see cref="DeploymentFailure" /> as it is everywhere else in this assembly.
/// </remarks>
public enum SignInResult
{
    /// <summary>The deployment accepted the credential, and the client is signed in.</summary>
    Accepted = 0,

    /// <summary>The deployment offers password sign-in and did not accept this username and password.</summary>
    /// <remarks>One case rather than two, because that is what the deployment answers with: its refusal is identical for an unknown username, a wrong password, a disabled credential, and a caller that has spent its attempts. A client that guessed which of them it was would be inventing a distinction the service deliberately does not make.</remarks>
    CredentialRefused = 1,

    /// <summary>The deployment does not offer password sign-in at all.</summary>
    /// <remarks>Read from the refusal's own challenge rather than guessed: a surface with the password method configured names it there, and one without it does not.</remarks>
    PasswordSignInNotOffered = 2,
}

/// <summary>Signs a person in to their deployment with their owner username and password, and holds the result.</summary>
/// <remarks>
/// <para>
/// HTTP Basic against the credential the deployment's own administrator provisioned, which is the one way into this
/// client. There is no authorization server to discover, no grant to redeem, and no token to renew: the password is
/// presented on every request, it stays valid until an administrator rotates it, and the session ends when somebody
/// signs out or when the deployment stops accepting it.
/// </para>
/// <para>
/// Signing in is one exchange: the credential is offered to the session route, which every caller may reach and which
/// answers about whatever was presented to it. That makes the answer authoritative in one request — an accepted
/// credential comes back as MailFathom's own session document, and a refused one as the surface's own challenge.
/// </para>
/// <para>
/// The offered credential is presented on a transport that carries no ambient one, so a candidate is never mixed with
/// whoever is already signed in, and a refused attempt leaves the running session exactly as it was.
/// </para>
/// </remarks>
public sealed class DeploymentSignIn
{
    private readonly IHttpClientFactory transports;
    private readonly DeploymentAddress address;
    private readonly SignedInOwner owner;

    /// <summary>Initializes the sign-in over the transports, the deployment reached, and the session it fills.</summary>
    /// <param name="transports">Supplies the credential-free transport a candidate is offered on.</param>
    /// <param name="address">Which deployment the credential is offered to.</param>
    /// <param name="owner">Who is signed in during this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>The transport is asked for per attempt rather than held, so the deployment this reaches is whichever one <see cref="DeploymentAddress" /> carries when somebody signs in rather than the one the host was composed against.</remarks>
    public DeploymentSignIn(IHttpClientFactory transports, DeploymentAddress address, SignedInOwner owner)
    {
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(owner);

        this.transports = transports;
        this.address = address;
        this.owner = owner;
    }

    /// <summary>Offers a credential to the deployment, and keeps it where it is accepted.</summary>
    /// <param name="credential">The username and password somebody typed.</param>
    /// <param name="cancellationToken">Abandons the attempt, which is not the same thing as it timing out.</param>
    /// <returns>What the deployment made of it, and what became of keeping it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <exception cref="DeploymentFailure">Thrown when nothing answered, nothing answered in time, or what answered is not MailFathom.</exception>
    public async Task<SignInAttempt> SignInAsync(
        OwnerCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var deployment = this.address.Current ?? throw new InvalidOperationException(
            "This client has not been pointed at a deployment, so there is nothing to sign in to. "
            + $"Point {nameof(DeploymentAddress)} at one before offering a credential.");

        var transport = this.transports.CreateClient(DeploymentHttpClients.SignIn);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(deployment, DeploymentRoutes.SessionPath))
        {
            Headers = { Authorization = BasicCredentialHeader.ComposedFrom(credential) },
        };

        using var response = await DeploymentExchange
            .SendAsync(transport, request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new SignInAttempt(
                BasicCredentialHeader.InvitesAPassword(response)
                    ? SignInResult.CredentialRefused
                    : SignInResult.PasswordSignInNotOffered,
                this.owner.Persistence);
        }

        DeploymentExchange.RefuseUnusableStatus(response);

        // Read rather than assumed from the status, for the reason the probe reads it: anything can answer 200 on a
        // port, and a captive portal that admits everything would otherwise sign somebody in to nothing.
        var reported = await DeploymentExchange
            .ReadBodyAsync(response, DeploymentJsonContext.Default.DeploymentSession, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(reported.Service, DeploymentProbe.ServiceName, StringComparison.Ordinal))
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "Something answered at that address, but it is not a MailFathom deployment.");
        }

        var persistence = await this.owner
            .AcceptAsync(deployment, credential, cancellationToken)
            .ConfigureAwait(false);

        return new SignInAttempt(SignInResult.Accepted, persistence);
    }

    /// <summary>Ends the session and clears whatever this head kept of it.</summary>
    /// <param name="cancellationToken">Abandons the removal, which does not un-end the session.</param>
    /// <returns>A task completing once nothing is held here or in the store.</returns>
    /// <remarks>Local, and it revokes nothing: HTTP Basic has no server-side session to end, so the password stays valid on the deployment until an administrator rotates it under <c>AdminEndpoint</c>.</remarks>
    public ValueTask SignOutAsync(CancellationToken cancellationToken = default) =>
        this.owner.ForgetAsync(cancellationToken);

    /// <summary>Restores a kept credential where this head kept one for the deployment it came up pointed at.</summary>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns><see langword="true" /> where the client is now signed in without anybody typing anything.</returns>
    /// <remarks>
    /// Asked once, after the deployment address has been restored and before anything is navigated to, so the client
    /// opens on the shell or on the sign-in screen rather than on a shell whose first request fails. It presents
    /// nothing to the deployment: a credential the deployment has since stopped accepting is discovered by the first
    /// request that carries it, which is the same answer a session that expired mid-run gets and is answered the same
    /// way.
    /// </remarks>
    public ValueTask<bool> RestoreAsync(CancellationToken cancellationToken = default) =>
        this.owner.RestoreAsync(this.address.Current, cancellationToken);
}
