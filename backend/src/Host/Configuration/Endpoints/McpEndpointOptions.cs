// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Quic;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Security.ClientCertificates;
using MailFathom.Mcp.Tools.Categories;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures whether the MCP protocol surface is served, where, and what a client must present to reach it.</summary>
/// <remarks>
/// <para>
/// Whether, where, to whom, and what it offers are the questions this section answers. The path is a constant published
/// by the protocol surface, and the transport is always stateless, because every MailFathom tool answers one request from the local
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
/// guards an enabled one is the list of <see cref="Authentication" /> methods, which starts empty, so authentication is
/// something an operator turns on; leaving it off is announced at startup rather than assumed to be intended. Each entry
/// carries the settings its own method needs, so a method cannot be selected without being configured or configured
/// without being selected, and the spellings that could have meant to turn one on — a misspelled key, an entry naming no
/// method, a value written where the list belongs — fail startup instead.
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

    private IReadOnlyList<string> retiredSettings = [];

    /// <summary>The half of the published permission vocabulary a grant on this endpoint draws from.</summary>
    /// <remarks>Stated once here rather than derived wherever a grant is read, so a permission belonging to the other surface is refused by the same rule everywhere the section is judged.</remarks>
    public const ProtectedSurface GrantedSurface = ProtectedSurface.Mail;

    /// <summary>Gets or sets whether the MCP endpoint is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so reaching a mailbox over MCP is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the IP address the clear-text listener binds, defaulting to every IPv4 address.</summary>
    /// <remarks>Use <c>127.0.0.1</c> to serve the endpoint to this machine only, one interface's address to restrict it to that interface, and <c>::</c> to bind IPv6, which on most systems accepts IPv4 connections as well.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the clear-text listener binds.</summary>
    /// <remarks>The default is the administrative endpoint's own as well, so enabling both surfaces without stating a port publishes one socket serving each of them rather than two. It is above 1024 so the process needs no privilege to bind it. Under <see cref="EndpointTransport.HttpsOnly" /> nothing binds it, because that mode opens no clear-text socket; the HTTPS profiles carry their own ports.</remarks>
    public int Port { get; set; } = 8080;

    /// <summary>Gets or sets which schemes the endpoint is served under.</summary>
    /// <remarks>Clear text unless a deployment states otherwise, which is what local development runs and what a deployment behind a TLS-terminating reverse proxy runs. Startup warns about it either way, because only an operator knows which of the two they have.</remarks>
    public EndpointTransport Transport { get; set; } = EndpointTransport.Http;

    /// <summary>Gets the methods a client may authenticate with, one entry per method with the conditions that method requires.</summary>
    /// <remarks>
    /// <para>
    /// Empty by default, which is the unauthenticated posture. A request is served when it satisfies any one of the
    /// entries, because the methods identify different kinds of caller rather than layering checks on one.
    /// </para>
    /// <para>
    /// An entry states which method is accepted and never who may use it. Every credential this endpoint judges names
    /// the owner whose mail it reaches, and an owner is a record in this deployment's database — so the keys, the
    /// public keys, the subjects, and the grants that used to be written here are provisioned through the
    /// administrative endpoint instead, and a section still carrying one is refused by name at startup.
    /// </para>
    /// </remarks>
    public IList<OwnerFacingAuthenticationOptions> Authentication { get; } = [];

    /// <summary>Gets the kinds of tool this endpoint publishes, empty to publish every one of them.</summary>
    /// <remarks>
    /// <para>
    /// The coarse answer to what this instance offers at all, beside the per-capability switches that decide what it can
    /// do. A category is named by <see cref="McpToolCategory" />, an unknown name fails startup rather than narrowing
    /// the endpoint to something no tool carries, and naming none publishes everything — so the setting's absence leaves
    /// an existing deployment exactly as it was.
    /// </para>
    /// <para>
    /// It only ever takes away. A category naming a capability this deployment has not enabled publishes nothing, and no
    /// entry here turns a capability on or widens a grant. A connecting client may narrow further still, through the
    /// header <see cref="McpToolCategoryHeader" /> defines, and the two compose as an intersection: what a client asks
    /// for is served only where this list already published it.
    /// </para>
    /// </remarks>
    public IList<string> PublishedToolCategories { get; } = [];

    /// <summary>Gets or sets which browser origins the endpoint answers.</summary>
    public TransportCorsOptions Cors { get; set; } = new();

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

    /// <summary>Gets or sets how long one request may run before the endpoint abandons it.</summary>
    /// <remarks>Defaulted throughout like <see cref="RateLimiting" /> and separate from it, because how much traffic is admitted and how long an admitted request may hold its permit are different questions a deployment answers independently.</remarks>
    public TransportRequestTimeoutOptions RequestTimeout { get; set; } = new();

    /// <summary>Gets whether a client may authenticate with a key this deployment provisioned for an owner.</summary>
    public bool AllowsApiKey => this.Accepts(OwnerCredentialMethod.ApiKey);

    /// <summary>Gets whether a client may authenticate with an access token from one of the configured authorization servers.</summary>
    public bool AllowsOAuth => this.Accepts(OwnerCredentialMethod.OAuthSubject);

    /// <summary>Gets whether a client may authenticate with an assertion signed by a public key an owner registered.</summary>
    public bool AllowsClientAssertion => this.Accepts(OwnerCredentialMethod.PublicKey);

    /// <summary>Gets whether a client may authenticate with an owner's own username and password.</summary>
    /// <remarks>What every one of these reports is that the endpoint accepts the method; which owners can actually use it is the credentials the administrative surface has provisioned, which is a question about the database rather than about this section.</remarks>
    public bool AllowsBasic => this.Accepts(OwnerCredentialMethod.Password);

    /// <summary>Gets whether a request must present a credential naming who is calling.</summary>
    /// <remarks>
    /// A client certificate profile is not one of these. A certificate names the application making the request, which
    /// is a different question from which person's mail is being served, so a deployment requiring a certificate and no
    /// authentication method still requires nothing in the sense this asks about — and still warns at startup.
    /// </remarks>
    public bool RequiresAuthentication => this.Authentication.Count > 0;

    /// <summary>Gets whether Kestrel terminates TLS for this endpoint.</summary>
    public bool TerminatesTls => TransportListenerConfiguration.TerminatesTls(this.Transport);

    /// <summary>Gets whether the clear-text listener answers every request with the address of the TLS one.</summary>
    public bool RedirectsClearText =>
        TransportListenerConfiguration.RedirectsClearText(this.Transport, this.Https.Redirect);

    /// <summary>Gets whether this surface answers its routes on a socket nothing encrypts.</summary>
    /// <remarks>Narrower than the negation of <see cref="TerminatesTls" />, and wider than it in the other direction: a mode that binds both sockets terminates TLS and still answers the routes in the clear unless the clear-text one redirects, and a clear-text socket that answers every request with the address of the TLS one carries no route at all.</remarks>
    public bool ServesClearText =>
        TransportListenerConfiguration.OpensClearTextListener(this.Transport) && !this.RedirectsClearText;

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

        // Read before the strict bind rather than after it. A retired key is a key this type no longer declares, so
        // binding first would raise the framework's own message about an unknown property — which says nothing about the
        // credential that replaced the setting, and that is the whole of what an operator upgrading has to be told.
        var retiredSettings = OwnerFacingAuthenticationConfiguration.FindRetiredSettingErrors(SectionName, section);

        if (retiredSettings.Count > 0)
        {
            return RefusingRetiredSettings(section, retiredSettings);
        }

        var settings = section.Get<McpEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new McpEndpointOptions();

        if (!section.GetSection($"{nameof(Cors)}:{nameof(TransportCorsOptions.AllowedOrigins)}").Exists())
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

        // Which key each entry was written under is read the same way and for the same reason, and both mail-serving
        // endpoints ask it through one method so the reading exists once.
        OwnerFacingAuthenticationConfiguration.ReadWhatTheBinderCannotSay(section, [.. settings.Authentication]);

        return settings;
    }

    /// <summary>Composes the settings a section carrying a retired key reads as, which is a refusal and one fact beside it.</summary>
    /// <remarks>
    /// The section was never bound, so nothing else it states was read; reporting anything beside the retired keys would
    /// be reporting defaults the operator did not write. <see cref="Enabled" /> is the exception, and it is read from
    /// configuration rather than left at its default: whether any surface is served at all is judged before any section
    /// answers for itself, so an endpoint the operator turned on that reported itself off would be refused for a process
    /// serving nothing — a statement about their configuration that is false — instead of for the retired key that is
    /// the whole of what an upgrade has to be told.
    /// </remarks>
    private static McpEndpointOptions RefusingRetiredSettings(IConfigurationSection section, IReadOnlyList<string> retiredSettings)
    {
        var refused = new McpEndpointOptions { Enabled = section.GetValue<bool>(nameof(Enabled)) };
        refused.retiredSettings = retiredSettings;

        return refused;
    }

    /// <summary>Reports what an access token must prove, once per entry that accepts one.</summary>
    /// <returns>The configured OAuth blocks, empty when the endpoint accepts no token.</returns>
    /// <remarks>A method rather than a property, because it reads the same objects the list already holds and a second path to them would leave which one a refusal names decided by the order reflection reports them in.</remarks>
    public IReadOnlyList<OAuthValidationOptions> OAuthMethods() =>
        OwnerFacingAuthenticationConfiguration.OAuthMethodsIn(this.Authentication);

    /// <summary>Reports the entry that accepts an owner's username and password, where the endpoint accepts one.</summary>
    /// <returns>The entry, or <see langword="null" /> when the endpoint accepts no password.</returns>
    /// <remarks>A method rather than a property, for the reason <see cref="OAuthMethods" /> is one.</remarks>
    public OwnerFacingAuthenticationOptions? BasicMethod() =>
        OwnerFacingAuthenticationConfiguration.BasicMethodIn(this.Authentication);

    private bool Accepts(OwnerCredentialMethod method) =>
        OwnerFacingAuthenticationConfiguration.Accepts(this.Authentication, method);

    /// <summary>Describes every socket this endpoint asks for.</summary>
    /// <returns>One declaration per socket, empty when the endpoint is not served.</returns>
    /// <remarks>Whether another surface asks for one of the same sockets, and whether the two agree about it, is <see cref="ListenerComposition" />'s question rather than this section's.</remarks>
    public IReadOnlyList<DeclaredListener> DeclareListeners() => this.Enabled
        ? TransportListenerConfiguration.DeclareListeners(
            SectionName,
            ServedSurfaces.Mcp,
            this.BindAddress,
            this.Port,
            this.Transport,
            this.Https,
            this.ClientCertificateProfiles.Count > 0)
        : [];

    /// <summary>Finds everything an operator must fix before the endpoint can be served.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <remarks>
    /// This covers the structure of the section only. Whether a configured key names itself usably, and whether the
    /// material behind it can be retrieved, are the secret machinery's questions and are answered by
    /// <see cref="SecretConfigurationValidator" /> against the same section.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        // Before the enabled question rather than after it, because a retired key is what stopped the section being
        // read at all: nothing below has values to judge, and a deployment that turned the endpoint off while leaving
        // one written has still not provisioned the credential that replaced it.
        if (this.retiredSettings.Count > 0)
        {
            return this.retiredSettings;
        }

        if (!this.Enabled)
        {
            return [];
        }

        var errors = new List<string>(OwnerFacingAuthenticationConfiguration.FindConfigurationErrors(
            SectionName,
            [.. this.Authentication]));

        errors.AddRange(this.FindPublishedToolCategoryErrors());

        errors.AddRange(this.Cors.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.Cors)}:{error}"));

        errors.AddRange(this.RateLimiting.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RateLimiting)}:{error}"));

        errors.AddRange(this.RequestTimeout.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RequestTimeout)}:{error}"));

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


        return errors;
    }


    /// <summary>Maps the configured profiles onto the ones a presented certificate is judged against.</summary>
    /// <returns>The trust profiles, in configuration order, empty when mutual TLS is off.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<McpClientCertificateTrustProfile> ToClientCertificateTrustProfiles() =>
        [.. this.ClientCertificateProfiles.Select(profile => profile.ToTrustProfile())];

    /// <summary>Maps the configured names onto the selection the protocol surface publishes by.</summary>
    /// <returns>The selection, which publishes every category when the deployment named none.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured name is not a published category, which <see cref="FindConfigurationErrors" /> refuses before composition reaches this.</exception>
    public PublishedToolCategorySelection ToPublishedToolCategories() => PublishedToolCategorySelection.Of(
        [
            .. this.PublishedToolCategories.Select(written => McpToolCategory.TryParse(written, out var category)
                ? category
                : throw new InvalidOperationException(
                    $"'{SectionName}:{nameof(this.PublishedToolCategories)}' names a category this surface does not publish, which startup validation refuses before the endpoint is composed.")),
        ]);

    /// <summary>Reports every configured name that no published category answers to.</summary>
    /// <remarks>
    /// Refused rather than ignored, for the reason every other unknown value in this section is: a misspelled category
    /// would narrow the endpoint to a name nothing carries, which is an endpoint publishing less than its operator
    /// wrote and saying nothing about it. The message names the value because a category name is MailFathom's own
    /// vocabulary rather than anybody's data, and lists what is accepted so the fix needs no second page.
    /// </remarks>
    private IEnumerable<string> FindPublishedToolCategoryErrors() => this.PublishedToolCategories
        .Index()
        .Where(written => !McpToolCategory.TryParse(written.Item, out _))
        .Select(written =>
            $"{SectionName}:{nameof(this.PublishedToolCategories)}:{written.Index} — '{written.Item}' is not a tool category this surface publishes. Write one of: {McpToolCategory.PublishedNames()}.");

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

}
