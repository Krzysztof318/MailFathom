// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Quic;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures whether the administrative surface is served, and what a client must present to reach it.</summary>
/// <remarks>
/// <para>
/// This is where an operator administers a running deployment from their own machine: signing in, and the operations
/// that follow. It is deliberately not the MCP endpoint with more routes on it. Reading a mailbox and administering the
/// service that reads it are different authorities, and keeping them on separate listeners with separate credentials is
/// what makes that a fact rather than a convention — an API key provisioned for an agent authenticates nothing here.
/// </para>
/// <para>
/// The endpoint is disabled by default, so a deployment that configures nothing serves no administrative surface at
/// all. What guards an enabled one is a set of <see cref="Authentication" /> methods that starts empty, so
/// authentication is something an operator turns on; leaving it off is announced at startup rather than assumed to be
/// intended, and the spellings that could have meant to turn it on — a misspelled key, a value naming no method — fail
/// startup instead.
/// </para>
/// <para>
/// There is no CORS section and no client-certificate profile here, and neither is an omission. The clients are command
/// line tools configured with an address, so no browser origin has anything to be told; and the trust question a
/// certificate answers is a second one this endpoint does not yet ask.
/// </para>
/// <para>
/// The value is read once, while the host is being composed, because whether an endpoint exists and what guards it are
/// part of the application's routing rather than something a request re-reads. A change takes effect on restart. The
/// material behind a configured key is a different matter and is resolved per request, so a key can be rotated in place
/// without one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AdminEndpointOptions
{
    /// <summary>The configuration section the endpoint settings are bound from.</summary>
    public const string SectionName = "AdminEndpoint";

    /// <summary>The path every administrative route is served beneath.</summary>
    /// <remarks>
    /// A constant rather than a setting, for the reason <see cref="Mcp.McpEndpointRoute.Path" /> is one: a client is
    /// configured with a host and a port and appends the rest, so a deployment that could move the prefix would only be
    /// able to move it in step with every client pointed at it. Publishing it here keeps the surface's address with the
    /// surface and leaves mapping it a decision the host still makes.
    /// <para>
    /// Two segments rather than one, so <c>/api</c> stays free for whatever else this deployment may serve later.
    /// Administering the service is one kind of API rather than the only one it could ever publish, and a prefix that
    /// claimed the whole of <c>/api</c> would have to be moved — breaking every configured client — the first time that
    /// stopped being true.
    /// </para>
    /// </remarks>
    public const string RoutePrefix = "/api/admin";

    private const TransportAuthenticationMethods KnownAuthenticationMethods =
        TransportAuthenticationMethods.ApiKey | TransportAuthenticationMethods.OAuth;

    /// <summary>Gets or sets whether the administrative surface is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so administering this service over the network is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the IP address the clear-text listener binds, used when <see cref="Https" /> names no profile.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the clear-text listener binds, used when <see cref="Https" /> names no profile.</summary>
    /// <remarks>The default is above 1024 so the process needs no privilege to bind it, and it is not a port any other listener here defaults to.</remarks>
    public int Port { get; set; } = 8090;

    /// <summary>Gets or sets which credentials a client may present, written as a set such as <c>ApiKey, OAuth</c>.</summary>
    /// <remarks>
    /// Empty by default, which is the unauthenticated posture. A request is served when it satisfies any one of the
    /// methods named here, because the methods identify different kinds of caller rather than layering checks on one.
    /// These are this endpoint's own methods: naming <c>ApiKey</c> here says nothing about the MCP endpoint and consults
    /// none of its keys.
    /// </remarks>
    public TransportAuthenticationMethods Authentication { get; set; } = TransportAuthenticationMethods.None;

    /// <summary>Gets the API keys a client may authenticate with, each a named secret with its own lifetime.</summary>
    /// <remarks>
    /// Several entries rather than one, so a key can be replaced by adding its successor, moving clients across, and
    /// removing the old entry — with both valid in between and no window in which nothing authenticates. An expired
    /// entry may stay in the list; it authenticates nothing and documents what was retired.
    /// </remarks>
    public IList<ConfiguredSecret> ApiKeys { get; } = [];

    /// <summary>Gets or sets which authorization servers may speak for this endpoint, and what a token must carry.</summary>
    /// <remarks>Configured separately from the MCP endpoint's even where both name one server, because the resource a token is issued for is what separates administering this service from reading a mailbox through it.</remarks>
    public OAuthValidationOptions OAuth { get; set; } = new();

    /// <summary>Gets or sets whether Kestrel terminates TLS for this endpoint, and under which domains and certificates.</summary>
    /// <remarks>
    /// Empty by default, which serves the endpoint over the clear-text listener <see cref="BindAddress" /> and
    /// <see cref="Port" /> name — which is the right posture behind a TLS-terminating reverse proxy and wrong anywhere
    /// else, so startup warns about it. Naming any profile takes the opposite posture in full: only those listeners
    /// bind, and no clear-text listener stays open behind them serving the same routes without the protection the
    /// profile was configured to add.
    /// </remarks>
    public TransportHttpsOptions Https { get; set; } = new();

    /// <summary>Gets whether a client may authenticate with one of the configured API keys.</summary>
    public bool AllowsApiKey => this.Authentication.HasFlag(TransportAuthenticationMethods.ApiKey);

    /// <summary>Gets whether a client may authenticate with an access token from one of the configured authorization servers.</summary>
    public bool AllowsOAuth => this.Authentication.HasFlag(TransportAuthenticationMethods.OAuth);

    /// <summary>Gets whether a request must present a credential naming who is calling.</summary>
    public bool RequiresAuthentication => this.Authentication != TransportAuthenticationMethods.None;

    /// <summary>Gets the ports this endpoint's listeners bind, which no other listener in the process may claim.</summary>
    public IReadOnlySet<int> ListenerPorts => this.Https.TerminatesTls
        ? this.Https.Endpoints.Select(static endpoint => endpoint.Port).ToHashSet()
        : new HashSet<int> { this.Port };

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>Strict binding is part of the read rather than something a caller opts into: this section is security-sensitive throughout, and a misspelled key that bound quietly would leave a decision reading as one nobody made.</remarks>
    public static AdminEndpointOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName)
            .Get<AdminEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new AdminEndpointOptions();
    }

    /// <summary>Finds everything an operator must fix before the endpoint can be served.</summary>
    /// <param name="portsClaimedElsewhere">The ports other listeners in this process bind, which this endpoint may not also claim.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portsClaimedElsewhere" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> FindConfigurationErrors(IReadOnlyCollection<int> portsClaimedElsewhere)
    {
        ArgumentNullException.ThrowIfNull(portsClaimedElsewhere);

        if (!this.Enabled)
        {
            return [];
        }

        var errors = new List<string>(this.FindAuthenticationErrors());

        errors.AddRange(this.FindListenerErrors(portsClaimedElsewhere));

        // The platform capability is read here rather than passed in, because whether this host can serve HTTP/3 is a
        // property of the machine the process is running on and not a decision composition takes.
        errors.AddRange(this.Https.FindConfigurationErrors(
            $"{SectionName}:{nameof(this.Https)}",
            QuicListener.IsSupported));

        return errors;
    }

    /// <summary>Refuses a listener that cannot bind, or that would take a socket another endpoint in this process needs.</summary>
    /// <remarks>
    /// A collision is reported here rather than left to the operating system, because an address-in-use failure names a
    /// socket and not the section that asked for it — and because two endpoints on one port would mean whichever bound
    /// first decided which credentials guarded the other's routes.
    /// </remarks>
    private IEnumerable<string> FindListenerErrors(IReadOnlyCollection<int> portsClaimedElsewhere)
    {
        if (!this.Https.TerminatesTls)
        {
            if (!IPAddress.TryParse(this.BindAddress?.Trim(), out _))
            {
                yield return $"{SectionName}:{nameof(this.BindAddress)} — state the IP address to bind, for example '0.0.0.0' for every IPv4 address, '127.0.0.1' to serve the administrative surface to this machine only, or '::' for IPv6.";
            }

            if (this.Port is < 1 or > 65535)
            {
                yield return $"{SectionName}:{nameof(this.Port)} — '{this.Port}' is not a TCP port; state a value between 1 and 65535.";
            }
        }

        foreach (var collidingPort in this.ListenerPorts.Where(portsClaimedElsewhere.Contains))
        {
            yield return $"{SectionName} — port {collidingPort} is already bound by another listener in this process, and the administrative surface is served on a listener of its own so that reaching it does not mean reaching the MCP endpoint. State a port nothing else binds.";
        }
    }

    private IEnumerable<string> FindAuthenticationErrors()
    {
        // The binder accepts any number for an enum, so 'Authentication=4' would bind to a set no member declares. Every
        // check below asks whether a particular method is among them, and such a value answers no to all of them: it
        // registers no authentication, requires no credential, and leaves the unauthenticated warning silent because it
        // is not None either. Refusing it here is what keeps a typo from opening the endpoint instead of closing it.
        var unknownMethods = this.Authentication & ~KnownAuthenticationMethods;
        if (unknownMethods != TransportAuthenticationMethods.None)
        {
            yield return $"{SectionName}:{nameof(this.Authentication)} — '{(int)unknownMethods}' names no authentication method; write '{nameof(TransportAuthenticationMethods.ApiKey)}', '{nameof(TransportAuthenticationMethods.OAuth)}', both separated by a comma, or '{nameof(TransportAuthenticationMethods.None)}'.";

            yield break;
        }

        foreach (var error in this.FindApiKeyErrors())
        {
            yield return error;
        }

        foreach (var error in this.FindOAuthErrors())
        {
            yield return error;
        }
    }

    private IEnumerable<string> FindApiKeyErrors()
    {
        if (this.AllowsApiKey && this.ApiKeys.Count == 0)
        {
            yield return $"{SectionName}:{nameof(this.ApiKeys)} — '{nameof(TransportAuthenticationMethods.ApiKey)}' authentication is selected and no key is configured, so no client could authenticate with one.";
        }

        // Configured-but-unchecked is refused rather than ignored in both directions below, because settings nothing
        // reads are a deployment believing it is protected — which is worse than one that knows it is not.
        if (!this.AllowsApiKey && this.ApiKeys.Count > 0)
        {
            yield return $"{SectionName}:{nameof(this.ApiKeys)} — API keys are configured while '{nameof(TransportAuthenticationMethods.ApiKey)}' is not among the authentication methods, so none of them is checked; add it or remove them.";
        }
    }

    private IEnumerable<string> FindOAuthErrors()
    {
        if (!this.AllowsOAuth)
        {
            if (this.OAuth.IsConfigured)
            {
                yield return $"{SectionName}:{nameof(this.OAuth)} — authorization servers are configured while '{nameof(TransportAuthenticationMethods.OAuth)}' is not among the authentication methods, so no token is checked against them; add it or remove the section.";
            }

            yield break;
        }

        foreach (var error in this.OAuth.FindConfigurationErrors())
        {
            yield return $"{SectionName}:{nameof(this.OAuth)}:{error}";
        }
    }
}
