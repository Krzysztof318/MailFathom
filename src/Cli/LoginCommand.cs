// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>Signs in to a deployment's administrative endpoint and remembers the credential.</summary>
/// <remarks>
/// <para>
/// The credential is verified before it is stored. A deployment that refuses it, an address that serves no
/// administrative endpoint, and a host that answers with something else all fail here rather than at the next command,
/// which is the difference between signing in and writing a file.
/// </para>
/// <para>
/// The credential is read from standard input rather than taken as an argument, because an argument reaches the shell
/// history, the process list, and any log of either. <c>--token-stdin</c> is therefore how it always arrives; a
/// terminal prompts for it and a script pipes it in.
/// </para>
/// </remarks>
internal static class LoginCommand
{
    /// <summary>Builds the <c>login</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("login", "Sign in to a deployment's administrative endpoint.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.ResolveEndpoint(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(CliContext context, Uri endpoint, CancellationToken cancellationToken)
    {
        var token = context.Console.ReadSecret(
            "Administrative credential (an API key, or an access token from the configured authorization server): ");

        if (token.Length == 0)
        {
            throw new CliFailure("No credential was supplied, so there is nothing to sign in with.");
        }

        using var transport = context.OpenTransport(endpoint);
        var session = await new AdminApiClient(transport).ReadSessionAsync(token, cancellationToken);

        context.Store.Save(endpoint, new StoredCredential(token, session.Credential ?? "unnamed"));

        context.Console.WriteLine(
            $"Signed in to {endpoint.GetLeftPart(UriPartial.Authority)} as '{session.Credential}' (MailFathom {session.Version}).");

        return CliExitCode.Success;
    }
}
