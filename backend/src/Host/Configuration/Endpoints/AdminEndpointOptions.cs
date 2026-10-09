// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Quic;
using MailFathom.Application.Access.DefaultAdministrator;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Mcp;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures whether the administrative surface is served, and which methods a client may sign in to it with.</summary>
/// <remarks>
/// <para>
/// This is where an operator administers a running deployment from their own machine: signing in, and the operations
/// that follow. It is deliberately not the MCP endpoint with more routes on it. Reading a mailbox and administering the
/// service that reads it are different authorities, and keeping them on separate listeners is what makes that a fact
/// rather than a convention — a credential provisioned for an agent states the mail-serving endpoints alone and
/// authenticates nothing here.
/// </para>
/// <para>
/// The endpoint is disabled by default, so a deployment that configures nothing serves no administrative surface at
/// all. An enabled one accepts the methods <see cref="Authentication" /> names, in the shape the MCP and client
/// endpoints use: the method and its transport conditions here, the person in the database. Every administrator is a
/// user, admitted while their grant holds an administrative permission at some scope; leaving the list empty serves
/// every caller as the default administrator, which startup announces rather than assumes was intended.
/// </para>
/// <para>
/// There is no client-certificate profile here, which is not an omission: the trust question a certificate answers is
/// a second one this endpoint does not yet ask. Where it is served is stated in exactly the settings the MCP endpoint
/// uses — <see cref="BindAddress" />, <see cref="Port" />, <see cref="Transport" />, and the profiles beneath
/// <see cref="Https" /> — so the day this endpoint does ask the trust question, the answer arrives as a profile on a
/// listener already shaped to carry one rather than as a second way to configure a socket.
/// <see cref="Cors" /> is the same section the other two surfaces carry, configured separately, defaulting to every
/// origin so a first run and a local orchestration answer a preflight; an operator who knows the origin they serve
/// names it.
/// </para>
/// <para>
/// The value is read once, while the host is being composed, because whether an endpoint exists and what guards it are
/// part of the application's routing rather than something a request re-reads. A change takes effect on restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AdminEndpointOptions
{
    /// <summary>The configuration section the endpoint settings are bound from.</summary>
    public const string SectionName = "AdminEndpoint";

    /// <summary>The half of the published permission vocabulary a grant on this endpoint draws from.</summary>
    /// <remarks>Stated once here rather than derived wherever a grant is read, so the half this surface reads of a user's grant is one decision.</remarks>
    public const ProtectedSurface GrantedSurface = ProtectedSurface.Administration;

    /// <summary>The path every administrative route is served beneath.</summary>
    /// <remarks>
    /// A constant rather than a setting, for the reason <see cref="McpEndpointRoute.Path" /> is one: a client is
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

    /// <summary>The setting that once declared the administrators in configuration, which a start now refuses.</summary>
    internal const string WithdrawnAdministratorsSetting = "Administrators";

    private IReadOnlyList<string> withdrawnSettings = [];

    /// <summary>Gets or sets whether the administrative surface is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so administering this service over the network is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the IP address the clear-text listener binds, defaulting to every IPv4 address.</summary>
    /// <remarks>Use <c>127.0.0.1</c> to administer this deployment from its own machine only, one interface's address to restrict it to that interface, and <c>::</c> to bind IPv6, which on most systems accepts IPv4 connections as well.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the clear-text listener binds.</summary>
    /// <remarks>The default is the MCP endpoint's own, so enabling both surfaces without stating a port publishes one socket serving each of them rather than two. It is above 1024 so the process needs no privilege to bind it. Under <see cref="EndpointTransport.HttpsOnly" /> nothing binds it, because that mode opens no clear-text socket; the HTTPS profiles carry their own ports.</remarks>
    public int Port { get; set; } = 8080;

    /// <summary>Gets or sets which schemes the endpoint is served under.</summary>
    /// <remarks>The same setting the MCP endpoint carries, read the same way. Clear text unless a deployment states otherwise, which is the right posture behind a TLS-terminating reverse proxy and wrong anywhere else, so startup warns about it.</remarks>
    public EndpointTransport Transport { get; set; } = EndpointTransport.Http;

    /// <summary>Gets the methods a client may sign in with, one entry per method with that method's own settings.</summary>
    /// <remarks>
    /// Empty by default, which is the unauthenticated posture: every caller is served as the default administrator. A
    /// request is served when a credential of one of these methods resolves a user, the credential lists this endpoint
    /// among its surfaces, and the user's grant holds an administrative permission at some scope. These are this
    /// endpoint's own methods, configured separately from the other endpoints' even where all of them name one
    /// authorization server, and the resource a token is issued for is what separates administering this service from
    /// reading a mailbox through it.
    /// </remarks>
    public IList<UserFacingAuthenticationOptions> Authentication { get; } = [];

    /// <summary>Gets or sets which browser origins the endpoint answers.</summary>
    /// <remarks>
    /// The same section the MCP and client endpoints carry, configured separately. A deployment that wrote no list
    /// serves every origin, because a surface is protected by the credential a caller presents rather than by which
    /// page called it, and because a first run that failed a preflight would look like a broken deployment. An empty
    /// list advertises nothing to a browser, which is what a deployment whose only clients are command-line tools
    /// wants.
    /// </remarks>
    public TransportCorsOptions Cors { get; set; } = new();

    /// <summary>Gets or sets under which domains and certificates Kestrel terminates TLS for this endpoint.</summary>
    /// <remarks>
    /// Read under the two <see cref="Transport" /> modes that terminate TLS and refused under the one that does not.
    /// <see cref="EndpointTransport.HttpsOnly" /> takes the TLS posture in full: only these listeners bind, and no
    /// clear-text listener stays open behind them serving the same administrative routes without the protection the
    /// profile was configured to add.
    /// </remarks>
    public TransportHttpsOptions Https { get; set; } = new();

    /// <summary>Gets or sets how much traffic the endpoint accepts before it starts refusing.</summary>
    /// <remarks>
    /// Unlike the settings above, every value in this section has a product default, so an endpoint an operator enabled
    /// is bounded whether or not they wrote a number — which is what stops an administrative surface reachable from the
    /// network from serving unbounded key guessing. It is the same section the MCP endpoint carries, configured
    /// separately: neither endpoint's limits reach the other's traffic.
    /// </remarks>
    public TransportRateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>Gets or sets how long one request may run before the endpoint abandons it.</summary>
    /// <remarks>The same section the MCP endpoint carries and configured separately, with the same default. An administrative request reaches no AI provider, so it is the one this can be narrowed on without asking what a tool call needs.</remarks>
    public TransportRequestTimeoutOptions RequestTimeout { get; set; } = new();

    /// <summary>Gets whether a client may authenticate with an API key provisioned for its user.</summary>
    public bool AllowsApiKey => this.Accepts(UserCredentialMethod.ApiKey);

    /// <summary>Gets whether a client may authenticate with an access token from one of the configured authorization servers.</summary>
    public bool AllowsOAuth => this.Accepts(UserCredentialMethod.OAuthSubject);

    /// <summary>Gets whether a client may authenticate with an assertion signed by a public key registered for its user.</summary>
    public bool AllowsClientAssertion => this.Accepts(UserCredentialMethod.PublicKey);

    /// <summary>Gets whether a client may authenticate with a user's own username and password.</summary>
    public bool AllowsBasic => this.Accepts(UserCredentialMethod.Password);

    /// <summary>Gets whether a request must present a credential naming who is calling.</summary>
    public bool RequiresAuthentication => this.Authentication.Count > 0;

    /// <summary>Gets whether Kestrel terminates TLS for this endpoint.</summary>
    public bool TerminatesTls => TransportListenerConfiguration.TerminatesTls(this.Transport);

    /// <summary>Gets whether the clear-text listener answers every request with the address of the TLS one.</summary>
    public bool RedirectsClearText =>
        TransportListenerConfiguration.RedirectsClearText(this.Transport, this.Https.Redirect);

    /// <summary>Gets the ports this endpoint's listeners bind, which no other listener in the process may claim.</summary>
    /// <remarks>Empty when the endpoint is not served at all. The clear-text port is one of them under every mode that opens that socket, whether it serves the routes or redirects away from them, so a deployment cannot give it to the probes or to the MCP surface and discover the conflict as an address-in-use error naming a socket rather than a section.</remarks>
    public IReadOnlySet<int> ListenerPorts => this.Enabled
        ? TransportListenerConfiguration.ListenerPorts(this.Transport, this.Port, this.Https)
        : new HashSet<int>();

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>Strict binding is part of the read rather than something a caller opts into: this section is security-sensitive throughout, and a misspelled key that bound quietly would leave a decision reading as one nobody made.</remarks>
    public static AdminEndpointOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        // Read before the strict bind rather than after it, for the reason the client section gives: a withdrawn key is
        // a key this type no longer declares, and the framework's own message about an unknown property says nothing
        // about what replaced it, which is the whole of what an operator upgrading has to be told.
        IReadOnlyList<string> withdrawnSettings =
        [
            .. FindWithdrawnAdministratorsErrors(section),
            .. UserFacingAuthenticationConfiguration.FindRetiredSettingErrors(SectionName, section),
        ];

        if (withdrawnSettings.Count > 0)
        {
            // Enabled is read beside the refusal, for the reason the client section reads it: whether any surface is
            // served is judged before a section answers for itself.
            return new AdminEndpointOptions
            {
                Enabled = section.GetValue<bool>(nameof(Enabled)),
                withdrawnSettings = withdrawnSettings,
            };
        }

        var settings = section.Get<AdminEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new AdminEndpointOptions();

        // The origin list is the one setting whose default cannot be a property initializer, for the reason the MCP
        // and client sections state: a collection the binder finds values for is added to rather than replaced, and an
        // empty JSON list binds identically to an absent one while meaning the opposite.
        if (!section.GetSection($"{nameof(Cors)}:{nameof(TransportCorsOptions.AllowedOrigins)}").Exists())
        {
            settings.Cors.ServeEveryBrowserOrigin();
        }

        // Read from configuration rather than from what was bound, because the redirect is on by default: an absent
        // section and one an operator wrote produce identical values, and only configuration can say which happened. It is
        // the difference between refusing a redirect configured for a surface that terminates no TLS and staying silent
        // about a default that surface never asked for.
        if (section.GetSection($"{nameof(Https)}:{nameof(TransportHttpsOptions.Redirect)}").Exists())
        {
            settings.Https.Redirect.MarkStated();
        }

        UserFacingAuthenticationConfiguration.ReadWhatTheBinderCannotSay(section, [.. settings.Authentication]);

        return settings;
    }

    /// <summary>Reports what an access token must prove, once per entry that accepts one.</summary>
    /// <returns>The configured OAuth blocks, empty when the endpoint accepts no token.</returns>
    /// <remarks>A method rather than a property, because it reads the same objects the list already holds and a second path to them would leave which one a refusal names decided by the order reflection reports them in.</remarks>
    public IReadOnlyList<OAuthValidationOptions> OAuthMethods() =>
        UserFacingAuthenticationConfiguration.OAuthMethodsIn(this.Authentication);

    /// <summary>Reports the entry that accepts a user's username and password, where the endpoint accepts one.</summary>
    /// <returns>The entry, or <see langword="null" /> when the endpoint accepts no password.</returns>
    /// <remarks>A method rather than a property, for the reason <see cref="OAuthMethods" /> is one.</remarks>
    public UserFacingAuthenticationOptions? BasicMethod() =>
        UserFacingAuthenticationConfiguration.BasicMethodIn(this.Authentication);

    /// <summary>Describes every socket this endpoint asks for.</summary>
    /// <returns>One declaration per socket, empty when the endpoint is not served.</returns>
    /// <remarks>Whether another surface asks for one of the same sockets, and whether the two agree about it, is <see cref="ListenerComposition" />'s question rather than this section's.</remarks>
    public IReadOnlyList<DeclaredListener> DeclareListeners() => this.Enabled
        ? TransportListenerConfiguration.DeclareListeners(
            SectionName,
            ServedSurfaces.Admin,
            this.BindAddress,
            this.Port,
            this.Transport,
            this.Https,
            requestsClientCertificates: false)
        : [];

    /// <summary>Finds everything an operator must fix before the endpoint can be served.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        // Before the enabled question rather than after it, because a withdrawn key is what stopped the section being read
        // at all, and a deployment that turned the endpoint off while leaving its administrators written has still not
        // recorded them as users.
        if (this.withdrawnSettings.Count > 0)
        {
            return this.withdrawnSettings;
        }

        if (!this.Enabled)
        {
            return [];
        }

        var authenticationErrors = UserFacingAuthenticationConfiguration.FindConfigurationErrors(
            SectionName,
            [.. this.Authentication]);

        var errors = new List<string>(authenticationErrors);

        if (authenticationErrors.Count == 0)
        {
            errors.AddRange(this.FindResourcePrefixErrors());
        }

        errors.AddRange(this.RateLimiting.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RateLimiting)}:{error}"));

        errors.AddRange(this.RequestTimeout.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RequestTimeout)}:{error}"));

        errors.AddRange(this.Cors.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.Cors)}:{error}"));

        // The platform capability is read here rather than passed in, because whether this host can serve HTTP/3 is a
        // property of the machine the process is running on and not a decision composition takes.
        errors.AddRange(TransportListenerConfiguration.FindConfigurationErrors(
            SectionName,
            this.BindAddress,
            this.Port,
            this.Transport,
            this.Https,
            QuicListener.IsSupported));

        return errors;
    }

    /// <summary>Refuses a configuration that still declares administrators, which nothing imports.</summary>
    /// <remarks>
    /// An administrator in configuration has no user to become: which person it was is the operator's to say, and an
    /// import guessing it would hand an administrative grant to whichever user the guess landed on. So the start stops
    /// and names what replaces the section instead.
    /// </remarks>
    private static IEnumerable<string> FindWithdrawnAdministratorsErrors(IConfigurationSection section)
    {
        if (!section.GetSection(WithdrawnAdministratorsSetting).Exists())
        {
            yield break;
        }

        yield return $"{SectionName}:{WithdrawnAdministratorsSetting} is no longer read: every administrator is a user holding an administrative role, and nothing imports what the section declared. Replace it with '{SectionName}:{UserFacingAuthenticationConfiguration.SettingName}' entries naming the methods this endpoint accepts — 'password' among them to sign in as the default administrator '{DefaultAdministratorBootstrap.Username}', whose password {DefaultAdministratorStartupGate.PasswordVariableName} sets on the first start — then provision each user who holds an administrative role a credential with 'mfctl credential create --surface {UserCredentialSurface.Administration.Name}', carry each entry's AllowedSourceNetworks onto it with '--source-network', and remove the section. Never remove it without writing those entries, because an endpoint accepting no method serves every caller as '{DefaultAdministratorBootstrap.Username}'.";
    }

    /// <summary>Reports the OAuth entries whose resource does not name the path these routes answer at.</summary>
    /// <remarks>
    /// A resource identifier is a name rather than an address to fetch, so nothing about OAuth requires it to match a
    /// route. What requires it here is discovery: <c>mfctl</c> is handed a host and a port and has to find the protected
    /// resource metadata document before it has read anything at all, which it can only do by appending the prefix it is
    /// about to call. That composition reaches the document's RFC 9728 location exactly when the resource names the same
    /// prefix, so a deployment whose resource says something else would publish a document nothing could find.
    /// <para>
    /// Read only once the shared rules have found nothing, because a resource this reads has to be one that parsed.
    /// </para>
    /// </remarks>
    private IEnumerable<string> FindResourcePrefixErrors()
    {
        foreach (var (index, method) in this.Authentication.Index())
        {
            if (method.OAuth is not { } oauth || NamesTheRoutePrefix(oauth))
            {
                continue;
            }

            yield return $"{UserFacingAuthenticationConfiguration.SettingPathOf(SectionName, method, index)}:{nameof(UserFacingAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.Resource)} — the path must be '{RoutePrefix}', because that is where the endpoint's routes answer and it is what a client appends to the address it was given. Write the absolute https URL clients reach this endpoint at, ending in that prefix.";
        }
    }

    private static bool NamesTheRoutePrefix(OAuthValidationOptions oauth) =>
        Uri.TryCreate(oauth.CanonicalResource(), UriKind.Absolute, out var resource)
        && string.Equals(resource.AbsolutePath.TrimEnd('/'), RoutePrefix, StringComparison.Ordinal);

    private bool Accepts(UserCredentialMethod method) =>
        UserFacingAuthenticationConfiguration.Accepts(this.Authentication, method);
}
