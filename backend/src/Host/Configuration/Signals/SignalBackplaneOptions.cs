// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Signals;

/// <summary>Declares the RESP endpoint one deployment's replicas carry client signals to each other over.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own rather than a block inside <see cref="ClientEndpointOptions" />, because what it
/// describes is a second server this deployment connects to — like the database and the object store, and unlike
/// everything else on that section, which describes the socket a client reaches. A root is also what gives the
/// endpoint's credential its own secret-name uniqueness scope.
/// </para>
/// <para>
/// <b>An absent section is the supported single-replica deployment</b> rather than a setting nobody got round to.
/// Nothing is registered, nothing is connected, and the host behaves exactly as it did before this section existed —
/// which is what a deployment running one replica should pay for a mechanism it does not need. What makes one
/// necessary is a second replica: an account is synchronized by whichever replica holds its lease, a client's
/// connection is wherever the load balancer put it, and without a backplane a signal raised on the first reaches
/// nothing held by the second.
/// </para>
/// <para>
/// The endpoint is named by a connection string rather than by a host and a port, because that is the shape every RESP
/// client, every managed provider, and every orchestrator already states one in — and it is a secret block rather
/// than a plain string because it carries the password. Which server answers it is the operator's: the local
/// orchestration starts Garnet when it is asked for one, and any Redis-compatible endpoint a deployment already runs is
/// as good.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SignalBackplaneOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "SignalBackplane";

    /// <summary>The channel prefix a deployment that states none carries its signals under.</summary>
    /// <remarks>The product's own name, so two MailFathom deployments sharing one endpoint collide by default and are made to say which is which, while a MailFathom beside somebody else's application never is.</remarks>
    public const string DefaultChannelPrefix = "mailfathom";

    private bool declared;

    /// <summary>Gets or sets the reference to the connection string the backplane endpoint is reached at.</summary>
    /// <remarks>
    /// Absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody
    /// wrote. It is required once the section exists at all: a section naming no endpoint describes nothing, and
    /// starting on it would leave an operator reading their own file as proof of a backplane that was never connected.
    /// The material is resolved when a connection is opened rather than while the host is composed, so a rotated
    /// password takes effect on the next reconnection with no restart to schedule.
    /// </remarks>
    public ConfiguredSecret? ConnectionString { get; set; }

    /// <summary>Gets or sets the prefix every channel this deployment publishes to and subscribes to begins with.</summary>
    /// <remarks>
    /// What keeps two applications sharing one RESP endpoint apart. Without distinct prefixes each one's statements
    /// reach the other's connections, which on this channel would mean one deployment's users being told to re-read
    /// another deployment's mail — so it defaults to a name rather than to nothing.
    /// </remarks>
    public string ChannelPrefix { get; set; } = DefaultChannelPrefix;

    /// <summary>Gets whether this deployment carries its signals over a backplane at all.</summary>
    /// <remarks>Read by the composition root to decide whether anything is registered. It reads the endpoint rather than the section, which is the same answer: a section that exists and names no endpoint is refused before this is asked.</remarks>
    public bool IsConfigured => this.ConnectionString is not null;

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings, which describe no backplane when the section is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>Strict binding is part of the read: a misspelled key here would leave a deployment that meant to scale out running without a backplane while its configuration said otherwise, and the symptom of that is a client quietly stopping being told things.</remarks>
    public static SignalBackplaneOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        var settings = section.Get<SignalBackplaneOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new SignalBackplaneOptions();

        // Whether the section exists is the one thing the binder cannot say: a section carrying nothing but a channel
        // prefix binds to the same object an absent one does, and the two mean opposite things — one is a deployment
        // that described no endpoint and has to be told so, the other is every deployment running a single replica.
        settings.declared = section.Exists();

        return settings;
    }

    /// <summary>Reports every reason this declaration could not be used, by reading it alone.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the section is usable or absent.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (!this.declared)
        {
            return [];
        }

        var errors = new List<string>();

        if (this.ConnectionString is null || string.IsNullOrWhiteSpace(this.ConnectionString.SecretReference))
        {
            errors.Add(Error(
                nameof(this.ConnectionString),
                "references no endpoint. The section exists to name the RESP server this deployment's replicas reach each other through, so one that names none is refused rather than started with a backplane that would never connect. Remove the section to run without one."));
        }

        if (string.IsNullOrWhiteSpace(this.ChannelPrefix))
        {
            errors.Add(Error(
                nameof(this.ChannelPrefix),
                $"is empty. It is what keeps two applications sharing one endpoint from receiving each other's statements, so leave it unset to take '{DefaultChannelPrefix}' rather than clearing it."));
        }

        return errors;
    }

    private static string Error(string propertyName, string detail) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}", SectionName, propertyName, detail);
}
