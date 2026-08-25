// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Transport;
using MailFathom.Common.OAuth;

namespace MailFathom.Cli.Commands;

/// <summary>Signs in to a deployment's administrative endpoint and remembers the credential under a name.</summary>
/// <remarks>
/// <para>
/// The credential is verified before it is stored. A deployment that refuses it, an address that serves no
/// administrative endpoint, and a host that answers with something else all fail here rather than at the next command,
/// which is the difference between signing in and writing a file. That holds whichever way the credential was obtained.
/// </para>
/// <para>
/// Four modes, and which one runs is stated rather than guessed. <see cref="SignInMode.Key" /> reads one opaque
/// credential from standard input, which is how an API key is presented and how a script signs in.
/// <see cref="SignInMode.KeyPair" /> names a private key on this machine and stores no credential at all: every later
/// command signs a fresh short-lived assertion with it, which is the mode a scheduled job wants, since the deployment
/// then holds only the public half. <see cref="SignInMode.Interactive" /> and <see cref="SignInMode.Device" /> are
/// OAuth: the command discovers where to authorize from the deployment itself, drives an authorization-code or
/// device-code grant, and stores the session the server issued. Guessing between them would mean a machine with no
/// browser sitting on a redirect that cannot arrive.
/// </para>
/// <para>
/// Signing in makes the new profile the default, because it is the deployment the operator just chose to work with.
/// <c>switch</c> is how that changes without signing in again.
/// </para>
/// </remarks>
internal static class LoginCommand
{
    /// <summary>The default loopback address an interactive sign-in catches the redirect on.</summary>
    /// <remarks>
    /// Written as <c>127.0.0.1</c> rather than <c>localhost</c>, because a name resolving to both an IPv4 and an IPv6
    /// address gives the browser two places to deliver the code and the listener one to wait on. RFC 8252 recommends a
    /// literal loopback address for exactly this reason.
    /// </remarks>
    private const string DefaultRedirectAddress = "http://127.0.0.1:8765/";

    /// <summary>Names how the operator produces the credential this deployment will accept.</summary>
    internal enum SignInMode
    {
        /// <summary>One opaque credential read from standard input: an API key, or an access token obtained elsewhere.</summary>
        Key = 0,

        /// <summary>An OAuth sign-in whose redirect comes back to a loopback address this command is listening on.</summary>
        Interactive = 1,

        /// <summary>An OAuth sign-in the person completes on another device, needing no browser on this machine.</summary>
        Device = 2,

        /// <summary>A private key on this machine, which every command signs a short-lived assertion with rather than presenting a stored credential.</summary>
        KeyPair = 3,
    }

    /// <summary>Builds the <c>login</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var nameOption = CliOptions.ProfileName();

        Option<SignInMode> modeOption = new("--mode")
        {
            Description = "How to sign in. Key reads an API key or an access token from standard input; key-pair signs each request with a private key on this machine; interactive opens a browser here and catches the redirect; device prints a code to enter on another device.",
            DefaultValueFactory = _ => SignInMode.Key,
        };

        Option<string> privateKeyOption = new("--private-key")
        {
            Description = "The private key the key-pair mode signs with, whose public half the deployment registers. The key is not copied into the credential store; its path is remembered and it is read on every command.",
        };

        Option<string> clientIdOption = new("--client-id")
        {
            Description = "The client identifier registered with the authorization server, required by the OAuth modes. The command is a public client and presents no secret.",
        };

        Option<string> issuerOption = new("--issuer")
        {
            Description = "Which authorization server to sign in at, needed only where the deployment accepts more than one.",
        };

        // A string rather than an Option<Uri>: System.CommandLine has no built-in conversion to Uri, so declaring one
        // makes every value on the command line fail to parse and leaves only the default reachable. Reading it here is
        // also what turns a mistyped address into one line an operator can act on rather than a parser's exception.
        Option<string> redirectUriOption = new("--redirect-uri")
        {
            Description = $"The loopback address registered with the authorization server, which the interactive mode catches the redirect on. Defaults to {DefaultRedirectAddress}.",
        };

        Option<bool> trustUntrustedCertificateOption = new(SignInConnection.AllowanceOption)
        {
            Description = "Accept whatever certificate this deployment presents at sign-in and pin it to the profile, without asking. For a sign-in with nobody at the terminal; every later command still refuses any other certificate.",
        };

        Option<bool> allowClearTextOption = new(ClearTextDecision.AllowanceOption)
        {
            Description = "Accept that an http:// endpoint carries the credential and every later request unprotected, without asking. For a sign-in with nobody at the terminal.",
        };

        Command command = new("login", "Sign in to a deployment's administrative endpoint.")
        {
            endpointOption,
            nameOption,
            modeOption,
            privateKeyOption,
            clientIdOption,
            issuerOption,
            redirectUriOption,
            trustUntrustedCertificateOption,
            allowClearTextOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(nameOption),
            new SignInRequest(
                result.GetValue(modeOption),
                result.GetValue(privateKeyOption),
                result.GetValue(clientIdOption),
                result.GetValue(issuerOption),
                result.GetValue(redirectUriOption),
                result.GetValue(trustUntrustedCertificateOption),
                result.GetValue(allowClearTextOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string? requestedName,
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        var (endpoint, profileName) = ResolveTarget(context, requestedDeployment, requestedName);

        // Both questions about the connection belong here: before anything is sent, and at the one command where a
        // decision about a deployment's transport is actually being taken. The clear-text one is settled from the
        // address alone; the certificate one can only be settled once the deployment has presented one, which is what
        // the connection does on its first use.
        var acceptsClearText = ClearTextDecision.Settle(context.Console, endpoint, request.AllowClearText);

        using SignInConnection connection = new(
            context.Console,
            context.OpenTransport,
            endpoint,
            acceptsClearText,
            request.TrustUntrustedCertificate);

        var (token, session, keyPair) = await ProduceCredentialAsync(context, connection, request, cancellationToken);

        // Whichever way the credential arrived, the deployment is what decides it is usable. Storing one it has not
        // accepted would turn a wrong client registration or a narrow scope into a failure at some later command. For a
        // key pair it also proves the deployment holds the matching public key, which nothing else on this machine can.
        var deploymentSession = await connection.RunAsync(
            transport => new AdminApiClient(transport, context.Console).ReadSessionAsync(token, cancellationToken));
        var credentialName = deploymentSession.Credential ?? "unnamed";

        // The minted assertion is deliberately not the stored token: it is spent within the minute and every later
        // command signs its own. What is stored for such a profile is where the key lives and nothing else.
        var placement = context.Store.Save(
            profileName,
            endpoint,
            keyPair is null ? token : string.Empty,
            credentialName,
            session,
            keyPair,
            connection.Trust);

        // Named here rather than by the access seam every other command goes through, because a sign-in establishes a
        // profile instead of resolving one — so without this the command that gives a deployment its name is the one
        // whose own record does not carry it.
        context.Invocation.ReachedDeployment(profileName);

        context.Console.WriteLine(
            $"Signed in to {endpoint.GetLeftPart(UriPartial.Authority)} as '{credentialName}' (MailFathom {deploymentSession.Version}), saved as profile '{profileName}' and selected.{DescribeTransport(connection.Trust)}");

        // Which of the two arrangements this machine offered, said at the one moment it is decided. A workstation with
        // a keyring and a jump host without one both keep working, and only the sentence tells them apart — so leaving
        // it out would mean an operator finding out what protects their credential by reading the file.
        if (placement.Describe() is { } storage)
        {
            context.Console.WriteNotice(storage);
        }

        // A sign-in that had to withdraw an entry, or replace a token profile with a key-pair one, can be refused by a
        // keyring that locked while it ran. What is left behind is a live credential under a profile whose file entry
        // no longer says the store holds anything, so nothing later goes looking for it and only the operator can.
        if (placement.Uncleared is { } uncleared)
        {
            context.Console.WriteWarning(SecretPlacement.DescribeUncleared(uncleared));
        }

        if (session is not null)
        {
            context.Console.WriteNotice(
                "The access token is renewed for you until the refresh token expires or is revoked, and the sign-in ends when it does.");
        }

        if (keyPair is not null)
        {
            context.Console.WriteNotice(
                $"No credential was stored. Every command signs a short-lived assertion with the key at {keyPair.PrivateKeyPath}, so keep that file readable by this account alone and the sign-in lasts as long as the deployment accepts its public half.");
        }

        return CliExitCode.Success;
    }

    /// <summary>Says what the connection this profile was signed in over is not protected by, when it is not.</summary>
    /// <remarks>On the confirmation line rather than beside it, because it qualifies what just happened: the operator accepted something a moment ago and the line that reports success is where they read what it was.</remarks>
    private static string DescribeTransport(StoredTransportTrust trust) => trust switch
    {
        { AcceptsClearText: true } =>
            " Nothing protects this connection: the credential and every later request cross the network in clear text.",
        { PinnedCertificateFingerprint: { } fingerprint } =>
            $" The connection is protected by a pinned certificate rather than by a chain this machine trusts; the profile now accepts {fingerprint} and refuses any other.",
        _ => string.Empty,
    };

    /// <summary>Produces the credential this sign-in verifies, in whichever of the four ways the operator asked for.</summary>
    /// <remarks>
    /// A key-pair sign-in returns both a credential to verify with and the key that produced it, because the two answer
    /// different questions: one proves the deployment accepts this client now, the other is what every later command
    /// will use.
    /// <para>
    /// The two ways that need no network run outside the connection, so the credential a person types is read once and
    /// a settled certificate question never costs them a second prompt for it.
    /// </para>
    /// </remarks>
    private static async Task<(string Token, OAuthSession? Session, StoredKeyPair? KeyPair)> ProduceCredentialAsync(
        CliContext context,
        SignInConnection connection,
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Mode == SignInMode.Key)
        {
            return (ReadPresentedCredential(context), null, null);
        }

        if (request.Mode == SignInMode.KeyPair)
        {
            var keyPair = ReadKeyPair(request.PrivateKeyPath);

            return (
                ClientAssertionCredential.MintFor(keyPair.PrivateKeyPath, context.Clock.GetUtcNow()),
                null,
                keyPair);
        }

        var (token, session) = await connection.RunAsync(
            transport => AuthorizeAsync(context, transport, request, cancellationToken));

        return (token, session, null);
    }

    /// <summary>Settles which private key a key-pair profile signs with.</summary>
    /// <remarks>
    /// The path is made absolute before it is stored, because a profile is used from whatever directory a later command
    /// runs in — including a scheduled job's, which is rarely the one the operator signed in from. A relative path that
    /// worked once would then fail with the key apparently missing.
    /// </remarks>
    private static StoredKeyPair ReadKeyPair(string? privateKeyPath)
    {
        if (privateKeyPath is not { Length: > 0 } path)
        {
            throw new CliFailure(
                "A key-pair sign-in needs the private key to sign with. Pass --private-key <path>, whose public half the deployment registers.");
        }

        return new StoredKeyPair(Path.GetFullPath(path));
    }

    /// <summary>Reads the credential an operator presents directly.</summary>
    /// <remarks>
    /// From standard input rather than as an argument, because an argument reaches the shell history, the process list,
    /// and any log of either. Reading it from there is unconditional: a terminal prompts for it without echoing, and a
    /// script pipes it in.
    /// </remarks>
    private static string ReadPresentedCredential(CliContext context)
    {
        var token = context.Console.ReadSecret(
            "Administrative credential (an API key, or an access token from the configured authorization server): ");

        return token.Length > 0
            ? token
            : throw new CliFailure("No credential was supplied, so there is nothing to sign in with.");
    }

    /// <summary>Runs an OAuth sign-in against whichever server the deployment names.</summary>
    private static async Task<(string Token, OAuthSession Session)> AuthorizeAsync(
        CliContext context,
        DeploymentTransport transport,
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClientId is not { Length: > 0 } clientId)
        {
            throw new CliFailure(
                "An OAuth sign-in needs the client identifier registered with the authorization server. Pass --client-id, or use --mode key to present an API key instead.");
        }

        var authorization = await new DeploymentAuthorizationDiscovery(transport, context.OpenUnpinnedTransport)
            .ReadAsync(request.Issuer, cancellationToken);

        // Aimed at the authorization server rather than at the deployment, through the same seam every other request
        // goes out by: a bounded per-request timeout, and redirects not followed.
        using var authorizationServerTransport = context.OpenUnpinnedTransport(authorization.TokenEndpoint);
        var authorizer = new DeploymentAuthorizer(authorizationServerTransport.Client, context.Clock);

        var grant = request.Mode == SignInMode.Device
            ? await AuthorizeWithDeviceAsync(context, authorizer, authorization, clientId, cancellationToken)
            : await AuthorizeInteractivelyAsync(context, authorizer, authorization, clientId, request, cancellationToken);

        return (
            grant.AccessToken,
            new OAuthSession(
                grant.RefreshToken,
                grant.AccessTokenExpiresAt,
                authorization.TokenEndpoint,
                authorization.Issuer,
                clientId,
                authorization.Resource,
                authorization.Scope));
    }

    /// <summary>Runs the sign-in with the redirect landing back on this machine.</summary>
    /// <remarks>
    /// The whole exchange happens on one screen: the address opens in the person's browser, the authorization server
    /// redirects to a loopback address this process is listening on, and the code arrives without being read off an
    /// address bar. The listener is bound <em>before</em> the address is shown, because a person who approves quickly
    /// would otherwise be redirected to a closed port.
    /// </remarks>
    private static async Task<DeploymentGrant> AuthorizeInteractivelyAsync(
        CliContext context,
        DeploymentAuthorizer authorizer,
        DeploymentAuthorization authorization,
        string clientId,
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        var redirectUri = ReadRedirectAddress(request.RedirectAddress);

        using var awaiter = context.AwaitRedirect(redirectUri);

        var pending = DeploymentAuthorizer.BuildAuthorization(authorization, clientId, redirectUri);

        context.Console.WriteNotice(string.Empty);
        context.Console.WriteNotice(context.OpenBrowser(pending.AuthorizationUrl)
            ? "A browser has been opened for you. If it did not appear, open this address yourself:"
            : "Open this address in a browser on this machine:");
        context.Console.WriteNotice(string.Empty);
        context.Console.WriteNotice($"  {pending.AuthorizationUrl}");
        context.Console.WriteNotice(string.Empty);
        context.Console.WriteNotice($"Waiting for the sign-in to come back to {redirectUri}...");

        var redirect = await awaiter.WaitForRedirectAsync(cancellationToken);

        if (redirect.Error is { } error)
        {
            throw new CliFailure(
                $"The authorization server refused the sign-in ('{AuthorizationServerErrorText.Sanitize(error)}').");
        }

        if (!pending.MatchesReturnedState(redirect.State))
        {
            throw new CliFailure(
                "The redirect did not echo the value this sign-in was started with, so it belongs to a different request and nothing was redeemed.");
        }

        if (redirect.Code is not { } authorizationCode)
        {
            throw new CliFailure("The redirect carried no authorization code, so there was nothing to redeem.");
        }

        return await authorizer.RedeemAuthorizationCodeAsync(
            authorization,
            clientId,
            pending,
            redirectUri,
            authorizationCode,
            cancellationToken);
    }

    private static Task<DeploymentGrant> AuthorizeWithDeviceAsync(
        CliContext context,
        DeploymentAuthorizer authorizer,
        DeploymentAuthorization authorization,
        string clientId,
        CancellationToken cancellationToken)
    {
        void ReportPrompt(DeviceCodePrompt prompt)
        {
            context.Console.WriteNotice(string.Empty);
            context.Console.WriteNotice("Open this address on any device with a browser:");
            context.Console.WriteNotice($"  {prompt.VerificationUriComplete ?? prompt.VerificationUri}");
            context.Console.WriteNotice(string.Empty);
            context.Console.WriteNotice($"and enter the code: {prompt.UserCode}");
            context.Console.WriteNotice($"The code expires at {prompt.ExpiresAt:u}. Waiting for the sign-in to complete...");
            context.Console.WriteNotice(string.Empty);
        }

        return authorizer.AuthorizeWithDeviceCodeAsync(authorization, clientId, ReportPrompt, cancellationToken);
    }

    /// <summary>Reads the address the authorization code comes back to.</summary>
    private static Uri ReadRedirectAddress(string? redirectAddress)
    {
        if (string.IsNullOrWhiteSpace(redirectAddress))
        {
            return new Uri(DefaultRedirectAddress);
        }

        return CliOptions.TryReadAddress(redirectAddress, out var parsed)
            ? parsed
            : throw new CliFailure(
                $"'{redirectAddress}' is not an address. Pass a redirect address such as {DefaultRedirectAddress}.");
    }

    /// <summary>Settles which address to sign in to and which name to remember it under.</summary>
    /// <remarks>
    /// An existing profile name is accepted as well as an address, which is how a credential is replaced when the
    /// deployment issues a new one: the operator names the profile they already have rather than retyping its address.
    /// </remarks>
    private static (Uri Endpoint, string ProfileName) ResolveTarget(
        CliContext context,
        string? requestedDeployment,
        string? requestedName)
    {
        if (requestedDeployment is null)
        {
            throw new CliFailure(
                $"No deployment was named. Pass --endpoint https://host:port, or set ${CliOptions.EndpointVariable}.");
        }

        if (CliOptions.TryReadAddress(requestedDeployment, out var address))
        {
            return (address, ValidProfileName(requestedName ?? address.Host));
        }

        var stored = context.Store.Read();

        if (!stored.Profiles.TryGetValue(requestedDeployment, out var existing))
        {
            throw new CliFailure(
                $"'{requestedDeployment}' is neither a stored profile nor an endpoint address. Pass --endpoint https://host:port to sign in to a deployment for the first time.");
        }

        return (
            new Uri(existing.Endpoint, UriKind.Absolute),
            ValidProfileName(requestedName ?? requestedDeployment));
    }

    /// <summary>Refuses a name that could not later be told apart from an address.</summary>
    /// <remarks><c>--endpoint</c> accepts both, and it separates them by whether the value is an absolute URI, so a profile named like one would be a profile nothing could select.</remarks>
    private static string ValidProfileName(string candidate)
    {
        var name = candidate.Trim();

        if (name.Length == 0)
        {
            throw new CliFailure("A profile name cannot be blank. Pass --name with the name to remember this deployment under.");
        }

        if (CliOptions.TryReadAddress(name, out _))
        {
            throw new CliFailure(
                $"'{name}' cannot be a profile name, because --endpoint reads an absolute address as an address rather than as a name. Choose a name such as 'production'.");
        }

        return name;
    }

    /// <summary>What the operator asked of one sign-in, beyond which deployment it is against.</summary>
    /// <param name="Mode">How the credential is produced.</param>
    /// <param name="PrivateKeyPath">The key a key-pair sign-in signs with, absent from every other mode.</param>
    /// <param name="ClientId">The client identifier for an OAuth sign-in, absent from a presented credential.</param>
    /// <param name="Issuer">Which authorization server to use, absent unless the deployment accepts several.</param>
    /// <param name="RedirectAddress">Where an interactive sign-in catches the redirect, absent to use the default.</param>
    /// <param name="TrustUntrustedCertificate">Whether the invocation stated up front that whatever certificate this deployment presents is to be accepted and pinned.</param>
    /// <param name="AllowClearText">Whether the invocation stated up front that an unprotected connection to this deployment is acceptable.</param>
    private sealed record SignInRequest(
        SignInMode Mode,
        string? PrivateKeyPath,
        string? ClientId,
        string? Issuer,
        string? RedirectAddress,
        bool TrustUntrustedCertificate,
        bool AllowClearText);
}
