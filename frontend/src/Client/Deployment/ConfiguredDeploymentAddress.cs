// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Reads the deployment's address out of what the installation states, which is what an installed head does.</summary>
/// <remarks>
/// <para>
/// The desktop head's answer, and the answer for any head that is installed rather than served. There is deliberately
/// no fallback: an application that quietly reached <c>localhost</c>, or the address of whoever wrote this file, would
/// be a mail client pointed somewhere nobody chose — so an installation stating nothing fails while it is starting,
/// naming the setting and where to write it, rather than opening a window that cannot explain itself.
/// </para>
/// <para>
/// HTTPS is the default rather than a requirement stated twice. A value written without a scheme is read as HTTPS,
/// because that is what a deployment reached across a network is served over and because the alternative — reading it
/// as clear text — would turn an omission into an exposure. Whether a stated clear-text address is acceptable at all is
/// <c>Client.Backend</c>'s rule and not repeated here: it permits loopback, which is where clear text is a development
/// posture, and refuses everything else because every request this client sends carries the signed-in token.
/// </para>
/// </remarks>
internal sealed class ConfiguredDeploymentAddress : IDeploymentAddressSource
{
    /// <summary>What a value with no scheme is read as.</summary>
    private const string DefaultScheme = "https";

    /// <inheritdoc />
    public Uri Resolve(DeploymentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var stated = settings.Address.Trim();

        if (stated.Length == 0)
        {
            throw new InvalidOperationException(
                $"This installation states no MailFathom deployment to reach. Write the address of yours as "
                + $"'{DeploymentSettings.SectionName}:{nameof(DeploymentSettings.Address)}' — for example "
                + "'https://mail.example.test' — in the appsettings.json this application reads.");
        }

        // Scheme-relative and schemeless are the same case here: the deployment is named by an origin, so anything
        // before the host that is not a scheme is not something this can repair.
        var addressed = stated.Contains("://", StringComparison.Ordinal)
            ? stated
            : $"{DefaultScheme}://{stated.TrimStart('/')}";

        if (!Uri.TryCreate(addressed, UriKind.Absolute, out var address))
        {
            throw new InvalidOperationException(
                $"'{settings.Address}' is not an address this application can reach a deployment at. Write "
                + $"'{DeploymentSettings.SectionName}:{nameof(DeploymentSettings.Address)}' as an origin — the scheme, "
                + "host, and port and nothing else — for example 'https://mail.example.test'.");
        }

        return address;
    }
}
