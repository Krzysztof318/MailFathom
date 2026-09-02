// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Whether this deployment serves the MailFathom client's bundle, and over what.</summary>
/// <remarks>
/// <para>
/// A bundle travels inside the container image and is served by the client endpoint's own listeners, which is what
/// makes the page and the surface it calls one origin: the bundle carries no address, resolves every route against
/// whatever served it, and a deployment therefore configures nothing on the client's side at all. What that removes is
/// the configuration question rather than the authorization one — a browser is an untrusted client wherever it was
/// served from, so a deployment whose endpoint requires a credential still requires one from the page.
/// </para>
/// <para>
/// Off unless a deployment says otherwise, which is what makes the bundle free to carry: every published image holds
/// one, and an installation that wants only the server pays its size and nothing else.
/// </para>
/// <para>
/// <see cref="AllowClearText" /> is the one setting here that is not a switch, and it exists because this hop is a
/// public one. The bundle is fetched by a browser rather than by a workload inside the network, so serving it over
/// plain HTTP publishes the application — and every token the page then attaches to a request — to anything on the
/// path. The refusal is declaration-based for the reason
/// <see cref="Providers.ProviderEndpointReachRules" /> is: this process can read the scheme of its own socket and
/// nothing beyond it, so a rule inferring safety from an address would refuse the reverse-proxy deployment it exists to
/// allow, and an operator who knows what stands in front of the process is the only one who can answer.
/// </para>
/// <para>
/// The section is read once, while the host is being composed, because whether the static files are in the pipeline is
/// part of the application's routing. A change takes effect on restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ClientApplicationOptions
{
    /// <summary>The file every request that matched no route falls back to, and what a present bundle is recognized by.</summary>
    /// <remarks>The bundle's entry document. Its presence is what separates an image carrying a bundle from one built without it, which is a distinction worth making at startup rather than as a page of 404s.</remarks>
    public const string EntryDocument = "index.html";

    /// <summary>Gets or sets whether the client's bundle is served from this deployment.</summary>
    /// <remarks>Served on the client endpoint's listeners and nowhere else, so it needs that endpoint enabled: same origin is the whole design, and a page served where the surface it calls is not would be a client that starts and cannot read a message.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets whether the operator has accepted that the page may reach a browser over plain HTTP.</summary>
    /// <remarks>
    /// <para>
    /// Off, so the secure posture is what a deployment gets without deciding anything. Turning it on is a statement
    /// about the whole path rather than about this process: it is correct behind a reverse proxy or an ingress that
    /// terminates TLS, and on a loopback bind for a first run on somebody's own machine, and it is wrong everywhere
    /// else.
    /// </para>
    /// <para>
    /// It says nothing about authentication, which the endpoint's own <c>Authentication</c> list decides and which a
    /// page is subject to exactly as any other client is.
    /// </para>
    /// </remarks>
    public bool AllowClearText { get; set; }
}
