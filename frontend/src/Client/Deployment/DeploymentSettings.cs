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
/// edits, so it reads both values from here. A browser head is served by the deployment it talks to, so its address is
/// the origin it was fetched from and this section's <see cref="Address" /> says nothing to it.
/// </para>
/// <para>
/// Read once, while the host is being composed. Nothing re-reads it: which deployment an installation talks to is not
/// something a running window changes.
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
    /// refused rather than dropped — <c>DeploymentOptions</c> throws while the host is being composed, naming the
    /// address — which is the loud half of the same rule that refuses an address stating nothing at all.
    /// A value with no scheme is read as HTTPS, which is the only scheme a deployment reached across a network may use;
    /// clear text is refused for anything but this machine, and refused by <c>Client.Backend</c> rather than here, so
    /// one rule judges every head.
    /// </remarks>
    public string Address { get; init; } = string.Empty;

    /// <summary>Gets the client identifier this application presents to the deployment's authorization server.</summary>
    /// <remarks>
    /// Public information rather than a secret, which is why it ships with a value: this is a public client, holds no
    /// secret, and every grant it makes is bound by a proof key instead. The default is the name MailFathom's own
    /// documentation asks an operator to register the client under, so an installation whose authorization server was
    /// set up from that page needs no edit here; one that registered another name writes it.
    /// </remarks>
    public string ClientId { get; init; } = string.Empty;
}
