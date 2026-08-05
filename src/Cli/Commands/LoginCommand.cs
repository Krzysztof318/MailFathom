// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
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
/// Three modes, and which one runs is stated rather than guessed. <see cref="SignInMode.Key" /> reads one opaque
/// credential from standard input, which is how an API key is presented and how a script signs in.
/// <see cref="SignInMode.Interactive" /> and <see cref="SignInMode.Device" /> are OAuth: the command discovers where to
/// authorize from the deployment itself, drives an authorization-code or device-code grant, and stores the session the
/// server issued. Guessing between them would mean a machine with no browser sitting on a redirect that cannot arrive.
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
            Description = "How to sign in. Key reads an API key or an access token from standard input; interactive opens a browser here and catches the redirect; device prints a code to enter on another device.",
            DefaultValueFactory = _ => SignInMode.Key,
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

        Command command = new("login", "Sign in to a deployment's administrative endpoint.")
        {
            endpointOption,
            nameOption,
            modeOption,
            clientIdOption,
            issuerOption,
            redirectUriOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            result.GetValue(nameOption),
            new SignInRequest(
                result.GetValue(modeOption),
                result.GetValue(clientIdOption),
                result.GetValue(issuerOption),
                result.GetValue(redirectUriOption)),
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

        using var transport = context.OpenTransport(endpoint);

        var (token, session) = request.Mode == SignInMode.Key
            ? (ReadPresentedCredential(context), null)
            : await AuthorizeAsync(context, transport, request, cancellationToken);

        // Whichever way the credential arrived, the deployment is what decides it is usable. Storing one it has not
        // accepted would turn a wrong client registration or a narrow scope into a failure at some later command.
        var deploymentSession = await new AdminApiClient(transport).ReadSessionAsync(token, cancellationToken);
        var credentialName = deploymentSession.Credential ?? "unnamed";

        context.Store.Save(profileName, endpoint, token, credentialName, session);

        context.Console.WriteLine(
            $"Signed in to {endpoint.GetLeftPart(UriPartial.Authority)} as '{credentialName}' (MailFathom {deploymentSession.Version}), saved as profile '{profileName}' and selected.");

        if (session is not null)
        {
            context.Console.WriteError(
                "The access token is renewed for you until the refresh token expires or is revoked, and the sign-in ends when it does.");
        }

        return CliExitCode.Success;
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
        HttpClient transport,
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClientId is not { Length: > 0 } clientId)
        {
            throw new CliFailure(
                "An OAuth sign-in needs the client identifier registered with the authorization server. Pass --client-id, or use --mode key to present an API key instead.");
        }

        var authorization = await new DeploymentAuthorizationDiscovery(transport)
            .ReadAsync(request.Issuer, cancellationToken);

        // Aimed at the authorization server rather than at the deployment, through the same seam every other request
        // goes out by: a bounded per-request timeout, and redirects not followed.
        using var authorizationServerTransport = context.OpenTransport(authorization.TokenEndpoint);
        var authorizer = new DeploymentAuthorizer(authorizationServerTransport, context.Clock);

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

        context.Console.WriteError(string.Empty);
        context.Console.WriteError(context.OpenBrowser(pending.AuthorizationUrl)
            ? "A browser has been opened for you. If it did not appear, open this address yourself:"
            : "Open this address in a browser on this machine:");
        context.Console.WriteError(string.Empty);
        context.Console.WriteError($"  {pending.AuthorizationUrl}");
        context.Console.WriteError(string.Empty);
        context.Console.WriteError($"Waiting for the sign-in to come back to {redirectUri}...");

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
            context.Console.WriteError(string.Empty);
            context.Console.WriteError("Open this address on any device with a browser:");
            context.Console.WriteError($"  {prompt.VerificationUriComplete ?? prompt.VerificationUri}");
            context.Console.WriteError(string.Empty);
            context.Console.WriteError($"and enter the code: {prompt.UserCode}");
            context.Console.WriteError($"The code expires at {prompt.ExpiresAt:u}. Waiting for the sign-in to complete...");
            context.Console.WriteError(string.Empty);
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
    /// <param name="ClientId">The client identifier for an OAuth sign-in, absent from a presented credential.</param>
    /// <param name="Issuer">Which authorization server to use, absent unless the deployment accepts several.</param>
    /// <param name="RedirectAddress">Where an interactive sign-in catches the redirect, absent to use the default.</param>
    private sealed record SignInRequest(
        SignInMode Mode,
        string? ClientId,
        string? Issuer,
        string? RedirectAddress);
}
