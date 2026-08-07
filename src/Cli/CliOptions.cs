// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>The options every command that reaches a deployment shares.</summary>
/// <remarks>
/// <para>
/// A command acts on the profile <c>switch</c> last selected, and <c>--endpoint</c> overrides that for one invocation
/// without changing it. The order is the option, then the environment variable, then the stored default: what an
/// operator typed beats what their shell was told, and both beat what they chose last time.
/// </para>
/// <para>
/// That order is applied here rather than by a configuration pipeline because <c>mfctl</c> composes none. It is a
/// command-line tool with three inputs and no settings file, so the precedence is short enough to state in one method
/// and is the whole of it; the host's own composed configuration governs the service, not the tool that talks to it.
/// </para>
/// </remarks>
internal static class CliOptions
{
    /// <summary>The environment variable naming the deployment, so a shell can state it once for a session.</summary>
    internal const string EndpointVariable = "MAILFATHOM_ENDPOINT";

    /// <summary>Builds the option naming which deployment to reach.</summary>
    /// <returns>The option.</returns>
    internal static Option<string?> Endpoint() => new("--endpoint")
    {
        Description = $"The deployment to act on for this invocation: a profile name, or an address such as https://mail.example.test:8443. Defaults to the profile last switched to, or ${EndpointVariable}.",
    };

    /// <summary>Builds the option naming the profile a sign-in is remembered under.</summary>
    /// <returns>The option.</returns>
    internal static Option<string?> ProfileName() => new("--name")
    {
        Description = "The name to remember this deployment under. Defaults to its host name.",
    };

    /// <summary>Reports which deployment the operator named for this invocation, if any.</summary>
    /// <param name="configuredEndpoint">What the operator passed to <c>--endpoint</c>, or <see langword="null" />.</param>
    /// <returns>A profile name, an address, or <see langword="null" /> to fall back to the stored default.</returns>
    internal static string? RequestedDeployment(string? configuredEndpoint) =>
        configuredEndpoint is { Length: > 0 } named
            ? named.Trim()
            : Environment.GetEnvironmentVariable(EndpointVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment.Trim()
                : null;

    /// <summary>Reads a value as an absolute endpoint address, when it is one.</summary>
    /// <param name="candidate">The value the operator wrote.</param>
    /// <param name="endpoint">The address, when the value is one.</param>
    /// <returns><see langword="true" /> when the value is an absolute HTTP or HTTPS address.</returns>
    /// <remarks>
    /// A value that is not one is read as a profile name rather than repaired into an address: prefixing a scheme onto
    /// a bare host would decide between a protected and an unprotected transport on the operator's behalf, and nothing
    /// about <c>production</c> says which they meant. Nothing can confuse the two, because a profile name is not an
    /// absolute URI.
    /// </remarks>
    internal static bool TryReadAddress(string? candidate, out Uri endpoint)
    {
        if (Uri.TryCreate(candidate?.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            endpoint = parsed;

            return true;
        }

        endpoint = null!;

        return false;
    }
}
