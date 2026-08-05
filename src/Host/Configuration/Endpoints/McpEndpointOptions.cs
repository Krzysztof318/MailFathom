// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Quic;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ClientCertificates;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures whether the MCP protocol surface is served, where, and what a client must present to reach it.</summary>
/// <remarks>
/// <para>
/// Whether, where, and to whom are the questions this section answers. The path is a constant published by the protocol
/// surface, and the transport is always stateless, because every MailFathom tool answers one request from the local
/// mailbox copy and needs no server-initiated message — which is the shape MCP deployments take today. Should a tool
/// that pushes notifications ever need sessions, that is a change to the surface rather than a knob an operator was
/// expected to find.
/// </para>
/// <para>
/// Where it is served is stated here and nowhere else. The endpoint binds its own listeners from
/// <see cref="BindAddress" />, <see cref="Port" />, and <see cref="Transport" />, in the same four settings the
/// administrative endpoint uses, so the process opens exactly the sockets its endpoint sections describe and the host's
/// own URL-shaped addresses decide nothing — <see cref="ExternalListenerConfiguration" /> refuses them rather than
/// letting one read as configured while nothing binds it.
/// </para>
/// <para>
/// The endpoint is disabled by default, so a deployment that configures nothing serves no mailbox over the network. What
/// guards an enabled one is a set of <see cref="Authentication" /> methods that starts empty, so authentication is
/// something an operator turns on; leaving it off is announced at startup rather than assumed to be intended, and the
/// spellings that could have meant to turn it on — a misspelled key, a value naming no method — fail startup instead.
/// </para>
/// <para>
/// The value is read once, while the host is being composed, because whether an endpoint exists and what guards it are
/// part of the application's routing rather than something a request re-reads. A change takes effect on restart; the
/// setting deliberately does not participate in configuration reload. The material behind a configured key is a
/// different matter and is resolved per request, so a key can be rotated in place without one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class McpEndpointOptions
{
    /// <summary>The configuration section the endpoint settings are bound from.</summary>
    public const string SectionName = "McpEndpoint";

    private const TransportAuthenticationMethods KnownAuthenticationMethods =
        TransportAuthenticationMethods.ApiKey | TransportAuthenticationMethods.OAuth;

    /// <summary>Gets or sets whether the MCP endpoint is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so reaching a mailbox over MCP is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the IP address the clear-text listener binds, defaulting to every IPv4 address.</summary>
    /// <remarks>Use <c>127.0.0.1</c> to serve the endpoint to this machine only, one interface's address to restrict it to that interface, and <c>::</c> to bind IPv6, which on most systems accepts IPv4 connections as well.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the clear-text listener binds.</summary>
    /// <remarks>The default is above 1024 so the process needs no privilege to bind it, and it is not a port any other listener here defaults to. Under <see cref="EndpointTransport.HttpsOnly" /> nothing binds it, because that mode opens no clear-text socket; the HTTPS profiles carry their own ports.</remarks>
    public int Port { get; set; } = 8080;

    /// <summary>Gets or sets which schemes the endpoint is served under.</summary>
    /// <remarks>Clear text unless a deployment states otherwise, which is what local development runs and what a deployment behind a TLS-terminating reverse proxy runs. Startup warns about it either way, because only an operator knows which of the two they have.</remarks>
    public EndpointTransport Transport { get; set; } = EndpointTransport.Http;

    /// <summary>Gets or sets which credentials a client may present, written as a set such as <c>ApiKey, OAuth</c>.</summary>
    /// <remarks>Empty by default, which is the unauthenticated posture. A request is served when it satisfies any one of the methods named here, because the methods identify different kinds of caller rather than layering checks on one.</remarks>
    public TransportAuthenticationMethods Authentication { get; set; } = TransportAuthenticationMethods.None;

    /// <summary>Gets the API keys a client may authenticate with, each a named secret with its own lifetime.</summary>
    /// <remarks>
    /// Several entries rather than one, so a key can be replaced by adding its successor, moving clients across, and
    /// removing the old entry — with both valid in between and no window in which nothing authenticates. An expired
    /// entry may stay in the list; it authenticates nothing and documents what was retired.
    /// </remarks>
    public IList<ConfiguredSecret> ApiKeys { get; } = [];

    /// <summary>Gets or sets what this deployment is called in OAuth terms, and which authorization servers may speak for it.</summary>
    public OAuthValidationOptions OAuth { get; set; } = new();

    /// <summary>Gets or sets which browser origins the endpoint answers.</summary>
    public McpCorsOptions Cors { get; set; } = new();

    /// <summary>Gets or sets under which domains and certificates Kestrel terminates TLS for this endpoint.</summary>
    /// <remarks>
    /// Read under the two <see cref="Transport" /> modes that terminate TLS and refused under the one that does not. It
    /// is what makes <see cref="ClientCertificateProfiles" /> reachable without a reverse proxy, because a client
    /// certificate is presented during a handshake this process has to be the one terminating.
    /// </remarks>
    public TransportHttpsOptions Https { get; set; } = new();

    /// <summary>Gets the client applications whose certificates the endpoint accepts, empty when mutual TLS is off.</summary>
    /// <remarks>
    /// Several named profiles rather than one certificate setting, so each client's policy is stated separately and one
    /// client's authority rotating cannot widen what another is trusted for. They compose with
    /// <see cref="Authentication" /> instead of replacing it, and a certificate reaches them only over an HTTPS
    /// endpoint — a deployment serving plain HTTP presents none, which a required profile refuses.
    /// </remarks>
    public IList<McpClientCertificateProfileOptions> ClientCertificateProfiles { get; } = [];

    /// <summary>Gets or sets how much traffic the endpoint accepts before it starts refusing.</summary>
    /// <remarks>Unlike the settings above, every value in this section has a product default, so an endpoint an operator enabled is bounded whether or not they wrote a number. The administrative endpoint configures its own copy of the same section, and neither endpoint's limits reach the other's traffic.</remarks>
    public TransportRateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>Gets whether a client may authenticate with one of the configured API keys.</summary>
    public bool AllowsApiKey => this.Authentication.HasFlag(TransportAuthenticationMethods.ApiKey);

    /// <summary>Gets whether a client may authenticate with an access token from one of the configured authorization servers.</summary>
    public bool AllowsOAuth => this.Authentication.HasFlag(TransportAuthenticationMethods.OAuth);

    /// <summary>Gets whether a request must present a credential naming who is calling.</summary>
    /// <remarks>
    /// A client certificate profile is not one of these. A certificate names the application making the request, which
    /// is a different question from which person's mail is being served, so a deployment requiring a certificate and no
    /// authentication method still requires nothing in the sense this asks about — and still warns at startup.
    /// </remarks>
    public bool RequiresAuthentication => this.Authentication != TransportAuthenticationMethods.None;

    /// <summary>Gets whether Kestrel terminates TLS for this endpoint.</summary>
    public bool TerminatesTls => TransportListenerConfiguration.TerminatesTls(this.Transport);

    /// <summary>Gets whether the clear-text listener answers every request with the address of the TLS one.</summary>
    public bool RedirectsClearText =>
        TransportListenerConfiguration.RedirectsClearText(this.Transport, this.Https.Redirect);

    /// <summary>Gets the ports this endpoint's listeners bind, which no other listener in the process may claim.</summary>
    /// <remarks>Empty when the endpoint is not served at all. The clear-text port is one of them under every mode that opens that socket, whether it serves the routes or redirects away from them, so a deployment cannot give it to the probes or to the administrative surface and discover the conflict as an address-in-use error naming a socket rather than a section.</remarks>
    public IReadOnlySet<int> ListenerPorts => this.Enabled
        ? TransportListenerConfiguration.ListenerPorts(this.Transport, this.Port, this.Https)
        : new HashSet<int>();

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings, with the defaults no binder can apply already applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Strict binding is part of the read rather than something a caller opts into: this section is security-sensitive
    /// throughout, and a misspelled key that bound quietly would leave a decision reading as one nobody made.
    /// <para>
    /// The origin list is the one setting whose default cannot be a property initializer. A collection the binder finds
    /// values for is added to rather than replaced, so a pre-populated default would survive beside the configured
    /// entries; and an empty JSON list binds identically to an absent one, which is why the two are told apart by asking
    /// configuration whether the key exists at all rather than by looking at what was bound.
    /// </para>
    /// </remarks>
    public static McpEndpointOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var settings = section.Get<McpEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new McpEndpointOptions();

        if (!section.GetSection($"{nameof(Cors)}:{nameof(McpCorsOptions.AllowedOrigins)}").Exists())
        {
            settings.Cors.ServeEveryBrowserOrigin();
        }

        // Read from configuration for the same reason the origin list above is: the redirect is on by default, so an
        // absent section and one an operator wrote bind to identical values, and only configuration can say which
        // happened. It is the difference between refusing a redirect configured for a surface that terminates no TLS and
        // staying silent about a default that surface never asked for.
        if (section.GetSection($"{nameof(Https)}:{nameof(TransportHttpsOptions.Redirect)}").Exists())
        {
            settings.Https.Redirect.MarkStated();
        }

        return settings;
    }

    /// <summary>Finds everything an operator must fix before the endpoint can be served.</summary>
    /// <param name="portsClaimedElsewhere">The ports other listeners in this process bind, which this endpoint may not also claim.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portsClaimedElsewhere" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This covers the structure of the section only. Whether a configured key names itself usably, and whether the
    /// material behind it can be retrieved, are the secret machinery's questions and are answered by
    /// <see cref="SecretConfigurationValidator" /> against the same section.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors(IReadOnlyCollection<int> portsClaimedElsewhere)
    {
        ArgumentNullException.ThrowIfNull(portsClaimedElsewhere);

        if (!this.Enabled)
        {
            return [];
        }

        var errors = new List<string>(this.FindAuthenticationErrors());

        errors.AddRange(this.Cors.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.Cors)}:{error}"));

        errors.AddRange(this.RateLimiting.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RateLimiting)}:{error}"));

        errors.AddRange(this.FindClientCertificateProfileErrors());

        // The platform capability is read here rather than passed in, because whether this host can serve HTTP/3 is a
        // property of the machine the process is running on and not a decision composition takes. The rules that do not
        // depend on it live in the shared listener rules, where a test can state both answers.
        errors.AddRange(TransportListenerConfiguration.FindConfigurationErrors(
            SectionName,
            this.BindAddress,
            this.Port,
            this.Transport,
            this.Https,
            QuicListener.IsSupported));

        errors.AddRange(this.FindListenerCollisions(portsClaimedElsewhere));

        return errors;
    }

    /// <summary>Refuses a listener that would take a socket another endpoint in this process needs.</summary>
    /// <remarks>A collision is reported here rather than left to the operating system, because an address-in-use failure names a socket and not the section that asked for it — and because two endpoints on one port would mean whichever bound first decided which credentials guarded the other's routes.</remarks>
    private IEnumerable<string> FindListenerCollisions(IReadOnlyCollection<int> portsClaimedElsewhere) =>
        this.ListenerPorts
            .Where(portsClaimedElsewhere.Contains)
            .Select(static collidingPort =>
                $"{SectionName} — port {collidingPort} is already bound by another listener in this process, and each surface is served on a listener of its own so that reaching one does not mean reaching another. State a port nothing else binds.");

    /// <summary>Maps the configured profiles onto the ones a presented certificate is judged against.</summary>
    /// <returns>The trust profiles, in configuration order, empty when mutual TLS is off.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<McpClientCertificateTrustProfile> ToClientCertificateTrustProfiles() =>
        [.. this.ClientCertificateProfiles.Select(profile => profile.ToTrustProfile())];

    /// <summary>Reports the profiles an operator must fix, and the names two of them share.</summary>
    /// <remarks>
    /// A duplicated name is refused rather than resolved by position, for the reason every other configured name is:
    /// the name is what a refusal in the log and an audit record are read by, and two profiles answering to one name
    /// make both records ambiguous.
    /// </remarks>
    private IEnumerable<string> FindClientCertificateProfileErrors()
    {
        // A client certificate is presented during a TLS handshake, so a transport that terminates none can never
        // receive one: every profile would sit unread while a deployment believed it had restricted who may call. It is
        // refused rather than warned about, because the two shapes it could mean — profiles nobody meant to keep, or a
        // transport nobody meant to leave clear — are both mistakes only an operator can settle.
        if (this.ClientCertificateProfiles.Count > 0 && !this.TerminatesTls)
        {
            yield return $"{SectionName}:{nameof(this.ClientCertificateProfiles)} — client certificate profiles are configured while '{SectionName}:{nameof(this.Transport)}' is '{this.Transport}', which terminates no TLS, so no certificate could ever be presented to judge against them. Select '{nameof(EndpointTransport.HttpAndHttps)}' or '{nameof(EndpointTransport.HttpsOnly)}', or remove the profiles.";
        }

        var claimedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, profile) in this.ClientCertificateProfiles.Index())
        {
            var profilePath = $"{SectionName}:{nameof(this.ClientCertificateProfiles)}:{index}";

            foreach (var error in profile.FindConfigurationErrors())
            {
                yield return $"{profilePath}:{error}";
            }

            if (!claimedNames.Add(profile.Name))
            {
                yield return $"{profilePath}:{nameof(McpClientCertificateProfileOptions.Name)} — another profile in this section already carries this name, so neither could be named unambiguously.";
            }
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
