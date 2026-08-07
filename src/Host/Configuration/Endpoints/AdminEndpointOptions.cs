// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
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
/// all. What guards an enabled one is the list of <see cref="Authentication" /> methods, which starts empty, so
/// authentication is something an operator turns on; leaving it off is announced at startup rather than assumed to be
/// intended. Each entry carries the settings its own method needs, so a method cannot be selected without being
/// configured or configured without being selected, and the spellings that could have meant to turn one on — a
/// misspelled key, an entry naming no method, a value written where the list belongs — fail startup instead.
/// </para>
/// <para>
/// There is no CORS section and no client-certificate profile here, and neither is an omission. The clients are command
/// line tools configured with an address, so no browser origin has anything to be told; and the trust question a
/// certificate answers is a second one this endpoint does not yet ask. Where it is served, however, is stated in
/// exactly the settings the MCP endpoint uses — <see cref="BindAddress" />, <see cref="Port" />,
/// <see cref="Transport" />, and the profiles beneath <see cref="Https" /> — so the day this endpoint does ask the trust
/// question, the answer arrives as a profile on a listener already shaped to carry one rather than as a second way to
/// configure a socket.
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

    /// <summary>Gets the credentials a client may present, one entry per authentication method with that method's own settings.</summary>
    /// <remarks>
    /// Empty by default, which is the unauthenticated posture. A request is served when it satisfies any one of the
    /// entries, because the methods identify different kinds of caller rather than layering checks on one. These are
    /// this endpoint's own methods, configured separately from the MCP endpoint's even where both name one authorization
    /// server: an entry here says nothing about that endpoint and consults none of its keys, and the resource a token is
    /// issued for is what separates administering this service from reading a mailbox through it.
    /// </remarks>
    public IList<TransportAuthenticationOptions> Authentication { get; } = [];

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

    /// <summary>Gets whether a client may authenticate with one of the configured API keys.</summary>
    public bool AllowsApiKey => this.ApiKeys().Count > 0;

    /// <summary>Gets whether a client may authenticate with an access token from one of the configured authorization servers.</summary>
    public bool AllowsOAuth => this.OAuthMethods().Count > 0;

    /// <summary>Gets whether a client may authenticate with an assertion signed by one of the configured public keys.</summary>
    public bool AllowsClientAssertion => this.PublicKeys().Count > 0;

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
        var settings = section.Get<AdminEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new AdminEndpointOptions();

        // Read from configuration rather than from what was bound, because the redirect is on by default: an absent
        // section and one an operator wrote produce identical values, and only configuration can say which happened. It is
        // the difference between refusing a redirect configured for a surface that terminates no TLS and staying silent
        // about a default that surface never asked for.
        if (section.GetSection($"{nameof(Https)}:{nameof(TransportHttpsOptions.Redirect)}").Exists())
        {
            settings.Https.Redirect.MarkStated();
        }

        return settings;
    }

    /// <summary>Reports every key a client may authenticate with, in configuration order.</summary>
    /// <returns>The configured keys, empty when the endpoint accepts none.</returns>
    /// <remarks>
    /// A method rather than a property, as <see cref="OAuthMethods" /> is, because it reads the same objects the list
    /// already holds. The secret machinery discovers what to resolve by walking this graph's readable properties, and a
    /// property here would offer it a second path to every configured key — leaving which of the two a refusal names
    /// decided by the order reflection happens to report them in.
    /// </remarks>
    public IReadOnlyList<ConfiguredSecret> ApiKeys() =>
        TransportAuthenticationConfiguration.ApiKeysIn(this.Authentication);

    /// <summary>Reports every client public key a signed assertion may be verified against, in configuration order.</summary>
    /// <returns>The configured public keys, empty when the endpoint accepts no assertion.</returns>
    /// <remarks>A method rather than a property, for the reason <see cref="ApiKeys" /> is one.</remarks>
    public IReadOnlyList<ConfiguredSecret> PublicKeys() =>
        TransportAuthenticationConfiguration.PublicKeysIn(this.Authentication);

    /// <summary>Reports what an access token must prove, once per entry that states OAuth.</summary>
    /// <returns>The configured OAuth blocks, empty when the endpoint accepts no token.</returns>
    public IReadOnlyList<OAuthValidationOptions> OAuthMethods() =>
        TransportAuthenticationConfiguration.OAuthMethodsIn(this.Authentication);

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
        if (!this.Enabled)
        {
            return [];
        }

        var authenticationErrors = TransportAuthenticationConfiguration.FindConfigurationErrors(
            SectionName,
            [.. this.Authentication]);

        var errors = new List<string>(authenticationErrors);

        if (authenticationErrors.Count == 0)
        {
            errors.AddRange(this.FindResourcePrefixErrors());
        }

        errors.AddRange(this.RateLimiting.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RateLimiting)}:{error}"));

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


    /// <summary>Reports the OAuth entries whose resource does not name the path these routes answer at.</summary>
    /// <remarks>
    /// A resource identifier is a name rather than an address to fetch, so nothing about OAuth requires it to match a
    /// route. What requires it here is discovery: <c>mfctl</c> is handed a host and a port and has to find the protected
    /// resource metadata document before it has read anything at all, which it can only do by appending the prefix it is
    /// about to call. That composition reaches the document's RFC 9728 location exactly when the resource names the same
    /// prefix, so a deployment whose resource says something else would publish a document nothing could find. Refused at
    /// startup rather than discovered by an operator whose sign-in reports that a deployment serves no metadata. It is
    /// the one thing this endpoint asks of a resource that no other surface does, which is why it belongs here rather
    /// than in the block itself.
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

            yield return $"{SectionName}:{TransportAuthenticationConfiguration.SettingName}:{index}:{nameof(TransportAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.Resource)} — the path must be '{RoutePrefix}', because that is where the endpoint's routes answer and it is what a client appends to the address it was given. Write the absolute https URL clients reach this endpoint at, ending in that prefix.";
        }
    }

    private static bool NamesTheRoutePrefix(OAuthValidationOptions oauth) =>
        Uri.TryCreate(oauth.CanonicalResource(), UriKind.Absolute, out var resource)
        && string.Equals(resource.AbsolutePath.TrimEnd('/'), RoutePrefix, StringComparison.Ordinal);
}
