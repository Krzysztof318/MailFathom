// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Quic;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures whether the client surface is served, and what a client must present to reach it.</summary>
/// <remarks>
/// <para>
/// This is where the MailFathom client reaches the service. It is deliberately not the MCP endpoint with more routes on
/// it and not the administrative endpoint with a wider grant: an agent's key, an operator's key, and the credential a
/// person signs the mail client in with are three separate things to provision and three separate things to revoke, and
/// keeping them on separate listeners with separate credentials is what makes that a fact rather than a convention.
/// </para>
/// <para>
/// What it draws on is the mailbox rather than a vocabulary of its own. <see cref="GrantedSurface" /> is
/// <see cref="ProtectedSurface.Mail" />, the same half the MCP endpoint's grants come from, because the client reads the
/// mail an agent reads and a second name for one authority would be two things to keep in step. Only the transport is
/// new.
/// </para>
/// <para>
/// The endpoint is disabled by default, so upgrading a deployment opens no new network door onto a mailbox. What guards
/// an enabled one is the list of <see cref="Authentication" /> methods, which starts empty, so authentication is
/// something an operator turns on; leaving it off is announced at startup rather than assumed to be intended. Each entry
/// carries the settings its own method needs, so a method cannot be selected without being configured or configured
/// without being selected, and the spellings that could have meant to turn one on — a misspelled key, an entry naming no
/// method, a value written where the list belongs — fail startup instead.
/// </para>
/// <para>
/// <see cref="Cors" /> is the one setting this section carries that the administrative one does not, and the difference
/// is the client rather than a preference: that endpoint's clients are command-line tools with no browser origin to be
/// told anything, while this one is called from a page and a preflight it cannot answer is a client that cannot start.
/// There is no client-certificate profile here, for the reason the administrative section states — the trust question a
/// certificate answers is a second one this endpoint does not yet ask — and where it is served is stated in exactly the
/// settings both existing endpoints use, so the day it does ask, the answer arrives as a profile on a listener already
/// shaped to carry one.
/// </para>
/// <para>
/// The value is read once, while the host is being composed, because whether an endpoint exists and what guards it are
/// part of the application's routing rather than something a request re-reads. A change takes effect on restart. The
/// material behind a configured key is a different matter and is resolved per request, so a key can be rotated in place
/// without one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ClientEndpointOptions
{
    /// <summary>The configuration section the endpoint settings are bound from.</summary>
    public const string SectionName = "ClientEndpoint";

    /// <summary>The half of the published permission vocabulary a grant on this endpoint draws from.</summary>
    /// <remarks>Stated once here rather than derived wherever a grant is read, so a permission belonging to the other surface is refused by the same rule everywhere the section is judged. It is the mailbox's half, which the MCP endpoint's grants also come from: the client reads the same mail, so it draws on the same vocabulary and only the transport is separate.</remarks>
    public const ProtectedSurface GrantedSurface = ProtectedSurface.Mail;

    /// <summary>The path every client route is served beneath.</summary>
    /// <remarks>
    /// A constant rather than a setting, for the reason <see cref="AdminEndpointOptions.RoutePrefix" /> is one: a client
    /// is configured with a host and a port and appends the rest, so a deployment that could move the prefix would only
    /// be able to move it in step with every client pointed at it.
    /// <para>
    /// It is the other API that section's remarks left <c>/api</c> free for. Two segments beside <c>/api/admin</c>
    /// rather than one, so administering the service and reading a mailbox from the client stay two prefixes an operator
    /// can route, publish, and refuse separately. There is no version segment: the major version is <c>0</c> and
    /// ADR 0004 permits breaking the contract outright, so <c>/v1</c> would be scaffolding for a promise this project
    /// has not made.
    /// </para>
    /// </remarks>
    public const string RoutePrefix = "/api/client";

    /// <summary>Gets or sets whether the client surface is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so serving a mailbox to a client over the network is always something an operator turned on — and an upgrade never opens a door onto mail without one.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the IP address the clear-text listener binds, defaulting to every IPv4 address.</summary>
    /// <remarks>Use <c>127.0.0.1</c> to serve the client on this machine only, one interface's address to restrict it to that interface, and <c>::</c> to bind IPv6, which on most systems accepts IPv4 connections as well.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the clear-text listener binds.</summary>
    /// <remarks>The default is the other two endpoints' own, so enabling several surfaces without stating a port publishes one socket serving each of them rather than three. It is above 1024 so the process needs no privilege to bind it. Under <see cref="EndpointTransport.HttpsOnly" /> nothing binds it, because that mode opens no clear-text socket; the HTTPS profiles carry their own ports.</remarks>
    public int Port { get; set; } = 8080;

    /// <summary>Gets or sets which schemes the endpoint is served under.</summary>
    /// <remarks>The same setting the other endpoints carry, read the same way. Clear text unless a deployment states otherwise, which is the right posture behind a TLS-terminating reverse proxy and wrong anywhere else, so startup warns about it.</remarks>
    public EndpointTransport Transport { get; set; } = EndpointTransport.Http;

    /// <summary>Gets the credentials a client may present, one entry per authentication method with that method's own settings.</summary>
    /// <remarks>
    /// Empty by default, which is the unauthenticated posture. A request is served when it satisfies any one of the
    /// entries, because the methods identify different kinds of caller rather than layering checks on one. These are
    /// this endpoint's own methods, configured separately from the other endpoints' even where all of them name one
    /// authorization server: an entry here says nothing about those endpoints and consults none of their keys, and the
    /// resource a token is issued for is what separates signing in to the mail client from reading the same mailbox as
    /// an agent.
    /// </remarks>
    public IList<TransportAuthenticationOptions> Authentication { get; } = [];

    /// <summary>Gets or sets which browser origins the endpoint answers.</summary>
    /// <remarks>The setting the administrative endpoint has no use for. A WebAssembly head calls this surface from a page origin, so a preflight this endpoint cannot answer is a client that never starts; the same section the MCP endpoint carries, configured separately.</remarks>
    public TransportCorsOptions Cors { get; set; } = new();

    /// <summary>Gets or sets under which domains and certificates Kestrel terminates TLS for this endpoint.</summary>
    /// <remarks>
    /// Read under the two <see cref="Transport" /> modes that terminate TLS and refused under the one that does not.
    /// <see cref="EndpointTransport.HttpsOnly" /> takes the TLS posture in full: only these listeners bind, and no
    /// clear-text listener stays open behind them serving the same client routes without the protection the profile was
    /// configured to add.
    /// </remarks>
    public TransportHttpsOptions Https { get; set; } = new();

    /// <summary>Gets or sets how much traffic the endpoint accepts before it starts refusing.</summary>
    /// <remarks>
    /// Unlike the settings above, every value in this section has a product default, so an endpoint an operator enabled
    /// is bounded whether or not they wrote a number — which is what stops a surface reachable from a page from serving
    /// unbounded key guessing. It is the same section the other endpoints carry, configured separately: no endpoint's
    /// limits reach another's traffic.
    /// </remarks>
    public TransportRateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>Gets or sets how long one request may run before the endpoint abandons it.</summary>
    /// <remarks>The same section the other endpoints carry and configured separately, with the same default.</remarks>
    public TransportRequestTimeoutOptions RequestTimeout { get; set; } = new();

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
    /// <remarks>Empty when the endpoint is not served at all. The clear-text port is one of them under every mode that opens that socket, whether it serves the routes or redirects away from them, so a deployment cannot give it to the probes or to another surface and discover the conflict as an address-in-use error naming a socket rather than a section.</remarks>
    public IReadOnlySet<int> ListenerPorts => this.Enabled
        ? TransportListenerConfiguration.ListenerPorts(this.Transport, this.Port, this.Https)
        : new HashSet<int>();

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings, with the defaults no binder can apply already applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>Strict binding is part of the read rather than something a caller opts into: this section is security-sensitive throughout, and a misspelled key that bound quietly would leave a decision reading as one nobody made.</remarks>
    public static ClientEndpointOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var settings = section.Get<ClientEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new ClientEndpointOptions();

        // The origin list is the one setting whose default cannot be a property initializer, for the reason the MCP
        // section states: a collection the binder finds values for is added to rather than replaced, and an empty JSON
        // list binds identically to an absent one while meaning the opposite.
        if (!section.GetSection($"{nameof(Cors)}:{nameof(TransportCorsOptions.AllowedOrigins)}").Exists())
        {
            settings.Cors.ServeEveryBrowserOrigin();
        }

        // Read from configuration for the same reason the origin list above is: the redirect is on by default, so an
        // absent section and one an operator wrote bind to identical values, and only configuration can say which
        // happened.
        if (section.GetSection($"{nameof(Https)}:{nameof(TransportHttpsOptions.Redirect)}").Exists())
        {
            settings.Https.Redirect.MarkStated();
        }

        // Each entry's grant is read the same way and for the same reason, and every endpoint asks it through one
        // method so the absent-versus-emptied reading exists once.
        TransportAuthenticationConfiguration.ReadWhatTheBinderCannotSay(section, [.. settings.Authentication]);

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
            ServedSurfaces.Client,
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
            [.. this.Authentication],
            GrantedSurface);

        var errors = new List<string>(authenticationErrors);

        if (authenticationErrors.Count == 0)
        {
            errors.AddRange(this.FindResourcePrefixErrors());
        }

        errors.AddRange(this.Cors.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.Cors)}:{error}"));

        errors.AddRange(this.RateLimiting.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RateLimiting)}:{error}"));

        errors.AddRange(this.RequestTimeout.FindConfigurationErrors()
            .Select(error => $"{SectionName}:{nameof(this.RequestTimeout)}:{error}"));

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
    /// The same rule the administrative endpoint applies and for the same reason: a client is handed an address and has
    /// to find the protected resource metadata document before it has read anything at all, which it can only do by
    /// appending the prefix it is about to call. That composition reaches the document's RFC 9728 location exactly when
    /// the resource names the same prefix, so a deployment whose resource says something else would publish a document
    /// nothing could find. It matters more here than there, because the reader is a page that cannot be told the
    /// address by hand.
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

            yield return $"{TransportAuthenticationConfiguration.SettingPathOf(SectionName, method, index)}:{nameof(TransportAuthenticationOptions.OAuth)}:{nameof(OAuthValidationOptions.Resource)} — the path must be '{RoutePrefix}', because that is where the endpoint's routes answer and it is what a client appends to the address it was given. Write the absolute https URL clients reach this endpoint at, ending in that prefix.";
        }
    }

    private static bool NamesTheRoutePrefix(OAuthValidationOptions oauth) =>
        Uri.TryCreate(oauth.CanonicalResource(), UriKind.Absolute, out var resource)
        && string.Equals(resource.AbsolutePath.TrimEnd('/'), RoutePrefix, StringComparison.Ordinal);
}
