// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>The options every command that reaches a deployment shares.</summary>
internal static class CliOptions
{
    /// <summary>The environment variable naming the deployment, so a shell can state it once for a session.</summary>
    internal const string EndpointVariable = "MAILFATHOM_ENDPOINT";

    /// <summary>Builds the option naming which deployment to reach.</summary>
    /// <returns>The option.</returns>
    internal static Option<string?> Endpoint() => new("--endpoint")
    {
        Description = $"The administrative endpoint, for example https://mail.example.test:8443. Defaults to ${EndpointVariable}.",
    };

    /// <summary>Settles which deployment a command reaches, from the option or the environment.</summary>
    /// <param name="configuredEndpoint">What the operator passed, or <see langword="null" /> when they passed nothing.</param>
    /// <returns>The endpoint address.</returns>
    /// <exception cref="CliFailure">Thrown when no endpoint was named, or the one named is not an absolute HTTP address.</exception>
    /// <remarks>
    /// The option wins over the environment, because an operator who typed an address meant that one. An address that
    /// is not absolute HTTP is refused rather than guessed at: prefixing a scheme onto a bare host would decide between
    /// a protected and an unprotected transport on the operator's behalf.
    /// </remarks>
    internal static Uri ResolveEndpoint(string? configuredEndpoint)
    {
        var candidate = configuredEndpoint is { Length: > 0 }
            ? configuredEndpoint
            : Environment.GetEnvironmentVariable(EndpointVariable);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new CliFailure(
                $"No deployment was named. Pass --endpoint https://host:port, or set ${EndpointVariable}.");
        }

        if (!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new CliFailure(
                $"'{candidate}' is not an endpoint address. Write the absolute address the administrative endpoint is served at, including the scheme, for example https://mail.example.test:8443.");
        }

        return endpoint;
    }
}
