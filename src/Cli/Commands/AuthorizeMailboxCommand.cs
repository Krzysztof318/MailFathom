// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Common.MailboxOAuth;
using MailFathom.Common.OAuth;

namespace MailFathom.Cli.Commands;

/// <summary>Builds the command that walks an operator through authorizing one mailbox.</summary>
/// <remarks>
/// <para>
/// The command produces a refresh token and prints it. It writes no configuration file and contacts no mail server:
/// provisioning the token is the operator's next step, through the same secret-reference mechanism every other
/// MailFathom credential arrives by, and the service reads it from there.
/// </para>
/// <para>
/// Three modes exist because the providers and the machines differ, rather than because an operator has a preference.
/// <see cref="AuthorizationMode.Interactive" /> is the default and the one to reach for: the command listens on the
/// loopback address the redirect is registered against, so approving in the browser completes the exchange with
/// nothing to copy. It is what running the command on the operator's own workstation buys, which is the ordinary case
/// now that the command administers a deployment over HTTP rather than living beside it.
/// </para>
/// <para>
/// <see cref="AuthorizationMode.Device" /> is RFC 8628 and needs no browser on this machine at all; Microsoft supports
/// it for the IMAP scopes and Google does not, because Google's device flow admits no mail scope.
/// <see cref="AuthorizationMode.Manual" /> is the last resort for a machine where neither holds: the operator opens a
/// printed address on whichever computer has a browser, and pastes back the code the failed redirect leaves in the
/// address bar.
/// </para>
/// </remarks>
internal static class AuthorizeMailboxCommand
{
    /// <summary>Names how the person completes the authorization.</summary>
    internal enum AuthorizationMode
    {
        /// <summary>The operator opens a printed address elsewhere and pastes the resulting code back.</summary>
        Manual = 0,

        /// <summary>The authorization server issues a short code the person enters on another device.</summary>
        Device = 1,

        /// <summary>The redirect comes back to a loopback address this command is listening on.</summary>
        Interactive = 2,
    }

    /// <summary>Builds the <c>authorize</c> command under a <c>mailbox</c> group.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command to add to the root.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Option<string> providerOption = new("--provider")
        {
            Description = $"Provider preset supplying the endpoints and scope: {string.Join(", ", MailProviderPreset.All.Select(preset => preset.PresetName))}.",
            Required = true,
        };

        Option<string> clientIdOption = new("--client-id")
        {
            Description = "The client identifier of the application registered with the provider.",
            Required = true,
        };

        Option<AuthorizationMode> modeOption = new("--mode")
        {
            Description = "How the person completes the sign-in. Interactive catches the redirect here; device needs no browser at all; manual prints an address to open elsewhere and asks for the code back.",
            DefaultValueFactory = _ => AuthorizationMode.Interactive,
        };

        Option<string> scopeOption = new("--scope")
        {
            Description = "Overrides the preset's scope. Rarely needed, and a wrong value produces a token the mail server rejects.",
        };

        // A string rather than an Option<Uri>: System.CommandLine has no built-in conversion to Uri, so declaring one
        // makes every value on the command line fail to parse and leaves only the default reachable. Reading it here is
        // also what turns a mistyped address into one line an operator can act on rather than a parser's exception.
        Option<string> redirectUriOption = new("--redirect-uri")
        {
            Description = "The loopback address registered with the provider, which the interactive and manual modes redirect to. Defaults to http://127.0.0.1:8765/.",
        };

        Option<bool> publicClientOption = new("--public-client")
        {
            Description = "Skips the client-secret prompt, for an application registered as a public client.",
        };

        Command command = new("authorize", "Obtain a mailbox refresh token to provision as a secret.")
        {
            providerOption,
            clientIdOption,
            modeOption,
            scopeOption,
            redirectUriOption,
            publicClientOption,
        };

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            context,
            parseResult.GetValue(providerOption)!,
            parseResult.GetValue(clientIdOption)!,
            parseResult.GetValue(modeOption),
            parseResult.GetValue(scopeOption),
            parseResult.GetValue(redirectUriOption),
            parseResult.GetValue(publicClientOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string providerName,
        string clientId,
        AuthorizationMode mode,
        string? scopeOverride,
        string? redirectAddress,
        bool isPublicClient,
        CancellationToken cancellationToken)
    {
        var redirectUri = ReadRedirectAddress(redirectAddress);

        if (!MailProviderPreset.TryParsePresetName(providerName, out var preset))
        {
            throw new CliFailure($"'{providerName}' is not a known provider preset.");
        }

        if (mode == AuthorizationMode.Device && preset.DeviceAuthorizationEndpoint is null)
        {
            throw new CliFailure(
                $"The {preset.PresetName} provider does not issue mail scopes through the device flow. Use --mode manual.");
        }

        // The preset knows whether the provider rejects an exchange carrying no client secret, and Google does. Without
        // this the flag is honored, the request goes out without the field, and the operator reads the authorization
        // server's own invalid_client instead of the reason — which is the same trade the device-flow guard above
        // exists to avoid.
        if (isPublicClient && preset.RequiresClientSecret)
        {
            throw new CliFailure(
                $"The {preset.PresetName} provider rejects an authorization that carries no client secret, so --public-client cannot be used with it. Register a confidential client and omit the flag.");
        }

        var clientSecret = isPublicClient
            ? null
            : context.Console.ReadSecret("Client secret (leave empty for a public client): ");

        var request = new MailboxAuthorizationRequest(
            preset.AuthorizationEndpoint,
            preset.TokenEndpoint,
            preset.DeviceAuthorizationEndpoint,
            clientId,
            string.IsNullOrEmpty(clientSecret) ? null : clientSecret,
            string.IsNullOrWhiteSpace(scopeOverride) ? preset.Scope : scopeOverride,
            redirectUri);

        // The transport is aimed at the authorization server rather than at a deployment, and it is the same seam every
        // other command reaches the network through: a bounded per-request timeout, and redirects not followed.
        using var transport = context.OpenTransport(preset.TokenEndpoint);
        var authorizer = new MailboxAuthorizer(transport, TimeProvider.System);

        try
        {
            var grant = mode switch
            {
                AuthorizationMode.Device => await AuthorizeWithDeviceAsync(context, authorizer, request, cancellationToken),
                AuthorizationMode.Manual => await AuthorizeManuallyAsync(context, authorizer, request, cancellationToken),
                _ => await AuthorizeInteractivelyAsync(context, authorizer, request, cancellationToken),
            };

            ReportGrant(context, grant);

            return CliExitCode.Success;
        }
        catch (MailboxAuthorizationFailedException failure)
        {
            throw new CliFailure(failure.Message, failure);
        }
        catch (HttpRequestException failure)
        {
            // The message is the transport's, not the authorization server's, so it carries no credential.
            throw new CliFailure($"The authorization server could not be reached: {failure.Message}", failure);
        }
    }

    private static Task<MailboxAuthorizationGrant> AuthorizeWithDeviceAsync(
        CliContext context,
        MailboxAuthorizer authorizer,
        MailboxAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var reportPrompt = new Progress<DeviceCodePrompt>(prompt =>
        {
            context.Console.WriteError(string.Empty);
            context.Console.WriteError("Open this address on any device with a browser:");
            context.Console.WriteError($"  {prompt.VerificationUriComplete ?? prompt.VerificationUri}");
            context.Console.WriteError(string.Empty);
            context.Console.WriteError($"and enter the code: {prompt.UserCode}");
            context.Console.WriteError($"The code expires at {prompt.ExpiresAt:u}. Waiting for the sign-in to complete...");
            context.Console.WriteError(string.Empty);
        });

        return authorizer.AuthorizeWithDeviceCodeAsync(request, reportPrompt, cancellationToken);
    }

    /// <summary>Reads the address the authorization code comes back to.</summary>
    /// <remarks>
    /// The default is written as <c>127.0.0.1</c> rather than <c>localhost</c>, because a name resolving to both an IPv4
    /// and an IPv6 address gives the browser two places to deliver the code and the listener one to wait on. Both
    /// providers accept a literal loopback address in a desktop-client registration, and RFC 8252 recommends it for
    /// exactly this reason.
    /// </remarks>
    private static Uri ReadRedirectAddress(string? redirectAddress)
    {
        if (string.IsNullOrWhiteSpace(redirectAddress))
        {
            return new Uri("http://127.0.0.1:8765/");
        }

        return CliOptions.TryReadAddress(redirectAddress, out var parsed)
            ? parsed
            : throw new CliFailure($"'{redirectAddress}' is not an address. Pass a redirect address such as http://127.0.0.1:8765/.");
    }

    /// <summary>Runs the authorization with the redirect landing back on this machine.</summary>
    /// <remarks>
    /// The whole exchange happens on one screen: the address opens in the person's browser, the authorization server
    /// redirects to a loopback address this process is listening on, and the code arrives without being read off an
    /// address bar. The listener is bound <em>before</em> the address is shown, because a person who approves quickly
    /// would otherwise be redirected to a closed port.
    /// </remarks>
    private static async Task<MailboxAuthorizationGrant> AuthorizeInteractivelyAsync(
        CliContext context,
        MailboxAuthorizer authorizer,
        MailboxAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        // Guarded rather than asserted: the request carries no redirect address for the device grant, and this mode is
        // the one that cannot proceed without one.
        var redirectUri = request.RedirectUri
            ?? throw new CliFailure("No redirect address was given, so the sign-in has nowhere to come back to. Pass --redirect-uri, or use --mode device.");

        using var awaiter = context.AwaitRedirect(redirectUri);

        var pending = authorizer.BuildAuthorization(request);

        context.Console.WriteError(string.Empty);
        context.Console.WriteError(context.OpenBrowser(pending.AuthorizationUrl)
            ? "A browser has been opened for you. If it did not appear, open this address yourself:"
            : "Open this address in a browser on this machine:");
        context.Console.WriteError(string.Empty);
        context.Console.WriteError($"  {pending.AuthorizationUrl}");
        context.Console.WriteError(string.Empty);
        context.Console.WriteError($"Waiting for the sign-in to come back to {redirectUri}...");

        var redirect = await awaiter.WaitForRedirectAsync(cancellationToken);

        if (redirect.Error is not null)
        {
            throw new MailboxAuthorizationFailedException(redirect.Error);
        }

        if (!pending.MatchesReturnedState(redirect.State))
        {
            throw new MailboxAuthorizationFailedException("state_mismatch");
        }

        if (redirect.Code is null)
        {
            throw new MailboxAuthorizationFailedException("no_authorization_code");
        }

        return await authorizer.RedeemAuthorizationCodeAsync(request, pending, redirect.Code, cancellationToken);
    }

    private static async Task<MailboxAuthorizationGrant> AuthorizeManuallyAsync(
        CliContext context,
        MailboxAuthorizer authorizer,
        MailboxAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var pending = authorizer.BuildAuthorization(request);

        context.Console.WriteError(string.Empty);
        context.Console.WriteError("Open this address in a browser, on any computer:");
        context.Console.WriteError(string.Empty);
        context.Console.WriteError($"  {pending.AuthorizationUrl}");
        context.Console.WriteError(string.Empty);
        context.Console.WriteError("After you approve access the browser is redirected to the registered address and will");
        context.Console.WriteError("most likely show a connection error. That is expected: nothing is listening there, and");
        context.Console.WriteError("the authorization code never leaves your machine. Copy the value of the 'code' query");
        context.Console.WriteError("parameter out of the address bar and paste it below.");
        context.Console.WriteError(string.Empty);

        var returnedState = context.Console.ReadSecret("The 'state' parameter from the same address: ");
        if (!pending.MatchesReturnedState(returnedState))
        {
            throw new MailboxAuthorizationFailedException("state_mismatch");
        }

        var authorizationCode = context.Console.ReadSecret("Authorization code: ");

        return await authorizer.RedeemAuthorizationCodeAsync(
            request,
            pending,
            authorizationCode.Trim(),
            cancellationToken);
    }

    /// <summary>Prints the refresh token to standard output, with the guidance around it on standard error.</summary>
    /// <remarks>
    /// The split is what makes the command usable in a pipeline: redirecting standard output captures the token alone,
    /// so an operator can write it straight into a file the deployment references without an editing step that would
    /// leave it in a shell history.
    /// </remarks>
    private static void ReportGrant(CliContext context, MailboxAuthorizationGrant grant)
    {
        context.Console.WriteError(string.Empty);
        context.Console.WriteError("Authorization succeeded. The refresh token follows on standard output.");
        context.Console.WriteError("Provision it as a secret and point the account's OAuth refresh-token reference at it;");
        context.Console.WriteError("do not paste it into a configuration file.");
        context.Console.WriteError(string.Empty);

        context.Console.WriteLine(grant.RefreshToken);
    }
}
