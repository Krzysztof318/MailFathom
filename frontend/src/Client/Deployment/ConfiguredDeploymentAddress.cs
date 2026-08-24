// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Reads the deployment's address out of what the installation states, which is what an installed head does.</summary>
/// <remarks>
/// <para>
/// The desktop head's answer, and the answer for any head that is installed rather than served. There is deliberately
/// no fallback: an application that quietly reached <c>localhost</c>, or the address of whoever wrote this file, would
/// be a mail client pointed somewhere nobody chose.
/// </para>
/// <para>
/// An installation stating nothing is not a failure any more. It used to be one, because the address was the whole of
/// what a head could know and a window it could not explain was worse than a message; now a person can say where their
/// MailFathom is while the application is running, so an installation that states nothing is simply one nobody has
/// configured, and the honest answer is <see langword="null" /> — the client asks. What is still a failure is an
/// installation that stated something unreadable, which is a value somebody wrote and would otherwise never learn was
/// ignored.
/// </para>
/// <para>
/// The scheme an address written without one is read as, and the reason it is HTTPS, are
/// <see cref="DeploymentAddressText" />'s and are shared with the screen a person types into.
/// </para>
/// </remarks>
internal sealed class ConfiguredDeploymentAddress : IDeploymentAddressSource
{
    /// <inheritdoc />
    public Uri? Resolve(DeploymentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Address.Trim().Length == 0)
        {
            return null;
        }

        return DeploymentAddressText.TryRead(settings.Address, out var address)
            ? address
            : throw new InvalidOperationException(
                $"'{settings.Address}' is not an address this application can reach a deployment at. Write "
                + $"'{DeploymentSettings.SectionName}:{nameof(DeploymentSettings.Address)}' as an origin — the scheme, "
                + "host, and port and nothing else — for example 'https://mail.example.test'.");
    }
}
