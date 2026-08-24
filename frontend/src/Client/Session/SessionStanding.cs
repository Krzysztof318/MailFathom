// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;

namespace MailFathom.Client.Session;

/// <summary>What the client made of the deployment's answer: the version it is running, and what may be offered.</summary>
/// <param name="DeploymentVersion">The version the deployment reported, which the client shows beside its own.</param>
/// <param name="Capabilities">Every capability the client knows how to offer, each with the standing it has here.</param>
/// <remarks>
/// <para>
/// The one place that answers <em>may this be offered</em>. A screen reads it rather than deriving the answer from a
/// request the deployment refused, which is what keeps the interface honest about the difference between a caller who
/// may not and an installation that cannot — and keeps every new screen from re-deriving both.
/// </para>
/// <para>
/// Every grant it reads is a statement about the signed-in owner and about nothing else the deployment holds. The
/// client surface resolves exactly one owner per request, so a permission reported here says what this person may do
/// with their own mail accounts; nothing composed from it may offer a view across the deployment.
/// </para>
/// </remarks>
public sealed record SessionStanding(
    string DeploymentVersion,
    IImmutableDictionary<ClientCapability, CapabilityStanding> Capabilities)
{
    /// <summary>The published permission each capability asks for, which is the client's end of one vocabulary.</summary>
    /// <remarks>
    /// The names are <c>docs/operations/permissions.md</c>'s, written here as literals for the reason
    /// <c>DeploymentRoutes</c> gives about the paths beside them: two ends stating one contract rather than a constant
    /// shared across the two stacks. Discover asks a question and is answered from mail, which is the grant behind
    /// <c>ask_mail</c>; Mail and Cases both read stored correspondence, and a Case is assembled out of it, so neither
    /// is offered to a caller who cannot read a mailbox.
    /// </remarks>
    private static readonly ImmutableDictionary<ClientCapability, string> RequiredGrant =
        new Dictionary<ClientCapability, string>
        {
            [ClientCapability.Discover] = "mailfathom.mail.ask",
            [ClientCapability.Mail] = "mailfathom.mail.read",
            [ClientCapability.Cases] = "mailfathom.mail.read",
        }.ToImmutableDictionary();

    /// <summary>Gets whether the session leaves this client with nothing at all to put in front of somebody.</summary>
    /// <remarks>Its own question rather than three read together, because the frame answers it with one sentence: a shell whose every space is withheld says so once instead of showing three empty ones.</remarks>
    public bool OffersNothing => !this.Any(CapabilityStanding.Offered);

    /// <summary>Every capability this client knows how to offer, whatever any one deployment does with it.</summary>
    /// <remarks>
    /// Derived from the enum rather than from the grant table, so a capability added without a grant behind it fails
    /// where it is composed instead of quietly never being offered.
    /// </remarks>
    internal static ImmutableHashSet<ClientCapability> EveryCapability { get; } =
        [.. Enum.GetValues<ClientCapability>()];

    /// <summary>Reads a deployment's answer as what this client may offer.</summary>
    /// <param name="reported">What the deployment said about the credential presented to it.</param>
    /// <returns>The standing of every capability the client knows.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reported" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Every capability is read as one this deployment provides, and the overload below is where that stops being
    /// assumed. The session document reports a service, a version, and a grant, and names no feature at all — see
    /// <c>docs/operations/client-endpoint.md</c> § <em>What it serves</em> — so nothing on the wire can say a
    /// capability is absent from this installation. Reading that silence as "provides everything" is the only safe
    /// reading of it: the alternative would withhold every space on every deployment, and a grant the deployment did
    /// report would then mean nothing.
    /// </para>
    /// <para>
    /// The distinction is modelled rather than inferred, which is the point. A later stage that gives the session
    /// something to say about the installation changes this method and no screen: what a screen already reads is
    /// <see cref="CapabilityStanding" />, and <see cref="CapabilityStanding.Unavailable" /> is already the answer it
    /// renders differently from <see cref="CapabilityStanding.Ungranted" />.
    /// </para>
    /// </remarks>
    public static SessionStanding Of(DeploymentSession reported) => Of(reported, EveryCapability);

    /// <summary>Reads a deployment's answer against a stated set of what that deployment provides.</summary>
    /// <param name="reported">What the deployment said about the credential presented to it.</param>
    /// <param name="provided">What this deployment can do at all, whoever is asking.</param>
    /// <returns>The standing of every capability the client knows.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static SessionStanding Of(DeploymentSession reported, IImmutableSet<ClientCapability> provided)
    {
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(provided);

        return new SessionStanding(
            reported.Version,
            EveryCapability.ToImmutableDictionary(
                capability => capability,
                capability => StandingOf(capability, reported, provided)));
    }

    /// <summary>Says why a capability is or is not offered here.</summary>
    /// <param name="capability">The capability to answer for.</param>
    /// <returns>Its standing, which is <see cref="CapabilityStanding.Unavailable" /> for one this client did not compose.</returns>
    public CapabilityStanding StandingOf(ClientCapability capability) =>
        this.Capabilities.TryGetValue(capability, out var standing)
            ? standing
            : CapabilityStanding.Unavailable;

    /// <summary>Reports whether anything this client knows how to offer stands the given way here.</summary>
    /// <param name="standing">The standing to look for.</param>
    /// <returns><see langword="true" /> where at least one capability has it.</returns>
    /// <remarks>
    /// What the frame says a withholding out of. Asked per standing rather than per capability, because the sentence a
    /// person needs is about the reason rather than about which space: <em>your credential does not permit some of
    /// this</em> is acted on by asking whoever runs the deployment, and <em>this deployment does not provide some of
    /// it</em> is acted on by not asking at all.
    /// </remarks>
    public bool Any(CapabilityStanding standing) => this.Capabilities.Values.Contains(standing);

    /// <summary>Reports whether the interface may put a capability in front of somebody.</summary>
    /// <param name="capability">The capability to answer for.</param>
    /// <returns><see langword="true" /> only where the deployment provides it and this caller's grant carries it.</returns>
    public bool Offers(ClientCapability capability) =>
        this.StandingOf(capability) is CapabilityStanding.Offered;

    private static CapabilityStanding StandingOf(
        ClientCapability capability,
        DeploymentSession reported,
        IImmutableSet<ClientCapability> provided)
    {
        if (!provided.Contains(capability))
        {
            return CapabilityStanding.Unavailable;
        }

        return reported.Grants(RequiredGrant[capability])
            ? CapabilityStanding.Offered
            : CapabilityStanding.Ungranted;
    }
}
