// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Which MailFathom deployment this application reaches, as whoever installed it states it.</summary>
/// <remarks>
/// <para>
/// The configuration half of what <c>Client.Backend</c> refuses to decide. That assembly has no default address and
/// composes none from a literal, because a client that guessed would reach somebody else's deployment; this is where
/// the head is told, and what it is told with depends on the head. A desktop head is installed next to a file somebody
/// edits, so it reads its address from here. A browser head is served by the deployment it talks to, so its address is
/// the origin it was fetched from and this section's <see cref="Address" /> says nothing to it.
/// </para>
/// <para>
/// Read once, while the host is being composed, and it is where the client <em>starts</em> rather than where it stays.
/// A person can point the application at a deployment while it is running, and what they choose is kept where a
/// restart finds it and is read before this — so an installation that stated an address is one that has been given a
/// first answer, not one that has been fixed to it.
/// </para>
/// </remarks>
internal sealed record DeploymentSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Deployment";

    /// <summary>Gets the deployment's address, as written, or empty when the installation states none.</summary>
    /// <remarks>
    /// An origin — scheme, host, and port — and nothing beneath it: MailFathom serves its client surface at a prefix
    /// this application already knows, so there is no sub-path deployment to state one for. A path written here is
    /// refused rather than dropped — the client fails while it is starting, naming the address — which is the loud half
    /// of a rule whose quiet half is an installation stating nothing at all, since that one is answered by asking
    /// whoever is using the application instead.
    /// A value with no scheme is read as HTTPS, which is the only scheme a deployment reached across a network may use;
    /// clear text is refused for anything but this machine, by <c>DeploymentAddressRule</c> rather than here, so one
    /// rule judges every address whoever wrote it.
    /// </remarks>
    public string Address { get; init; } = string.Empty;
}
