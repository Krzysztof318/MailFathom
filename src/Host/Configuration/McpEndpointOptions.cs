// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Net.Quic;
using MailMcp.Infrastructure.Secrets;
using MailMcp.Infrastructure.Security;

namespace MailMcp.Host.Configuration;

/// <summary>Configures whether the MCP protocol surface is served, and what a client must present to reach it.</summary>
/// <remarks>
/// <para>
/// Whether and to whom are the questions this section answers. The path is a constant published by the protocol
/// surface, and the transport is always stateless, because every MailMcp tool answers one request from the local
/// mailbox copy and needs no server-initiated message — which is the shape MCP deployments take today. Should a tool
/// that pushes notifications ever need sessions, that is a change to the surface rather than a knob an operator was
/// expected to find.
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

    private const McpTransportAuthenticationMethods KnownAuthenticationMethods =
        McpTransportAuthenticationMethods.ApiKey | McpTransportAuthenticationMethods.OAuth;

    /// <summary>Gets or sets whether the MCP endpoint is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so reaching a mailbox over MCP is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets which credentials a client may present, written as a set such as <c>ApiKey, OAuth</c>.</summary>
    /// <remarks>Empty by default, which is the unauthenticated posture. A request is served when it satisfies any one of the methods named here, because the methods identify different kinds of caller rather than layering checks on one.</remarks>
    public McpTransportAuthenticationMethods Authentication { get; set; } = McpTransportAuthenticationMethods.None;

    /// <summary>Gets the API keys a client may authenticate with, each a named secret with its own lifetime.</summary>
    /// <remarks>
    /// Several entries rather than one, so a key can be replaced by adding its successor, moving clients across, and
    /// removing the old entry — with both valid in between and no window in which nothing authenticates. An expired
    /// entry may stay in the list; it authenticates nothing and documents what was retired.
    /// </remarks>
    public IList<ConfiguredSecret> ApiKeys { get; } = [];

    /// <summary>Gets or sets what this deployment is called in OAuth terms, and which authorization servers may speak for it.</summary>
    public McpOAuthOptions OAuth { get; set; } = new();

    /// <summary>Gets or sets which browser origins the endpoint answers.</summary>
    public McpCorsOptions Cors { get; set; } = new();

    /// <summary>Gets or sets whether Kestrel terminates TLS for this endpoint, and under which domains and certificates.</summary>
    /// <remarks>
    /// Empty by default, which serves the endpoint over the host's ordinary listener — clear text unless something in
    /// front supplies TLS. It is what makes <see cref="ClientCertificateProfiles" /> reachable without a reverse proxy,
    /// because a client certificate is presented during a handshake this process has to be the one terminating.
    /// </remarks>
    public McpHttpsOptions Https { get; set; } = new();

    /// <summary>Gets the client applications whose certificates the endpoint accepts, empty when mutual TLS is off.</summary>
    /// <remarks>
    /// Several named profiles rather than one certificate setting, so each client's policy is stated separately and one
    /// client's authority rotating cannot widen what another is trusted for. They compose with
    /// <see cref="Authentication" /> instead of replacing it, and a certificate reaches them only over an HTTPS
    /// endpoint — a deployment serving plain HTTP presents none, which a required profile refuses.
    /// </remarks>
    public IList<McpClientCertificateProfileOptions> ClientCertificateProfiles { get; } = [];

    /// <summary>Gets or sets how much traffic the endpoint accepts before it starts refusing.</summary>
    /// <remarks>Unlike the settings above, every value in this section has a product default, so an endpoint an operator enabled is bounded whether or not they wrote a number.</remarks>
    public McpRateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>Gets whether a client may authenticate with one of the configured API keys.</summary>
    public bool AllowsApiKey => this.Authentication.HasFlag(McpTransportAuthenticationMethods.ApiKey);

    /// <summary>Gets whether a client may authenticate with an access token from one of the configured authorization servers.</summary>
    public bool AllowsOAuth => this.Authentication.HasFlag(McpTransportAuthenticationMethods.OAuth);

    /// <summary>Gets whether a request must present a credential naming who is calling.</summary>
    /// <remarks>
    /// A client certificate profile is not one of these. A certificate names the application making the request, which
    /// is a different question from which person's mail is being served, so a deployment requiring a certificate and no
    /// authentication method still requires nothing in the sense this asks about — and still warns at startup.
    /// </remarks>
    public bool RequiresAuthentication => this.Authentication != McpTransportAuthenticationMethods.None;

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

        return settings;
    }

    /// <summary>Finds everything an operator must fix before the endpoint can be served.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <remarks>
    /// This covers the structure of the section only. Whether a configured key names itself usably, and whether the
    /// material behind it can be retrieved, are the secret machinery's questions and are answered by
    /// <see cref="SecretConfigurationValidator" /> against the same section.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
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
        // depend on it live in the section itself, where a test can state both answers.
        errors.AddRange(this.Https.FindConfigurationErrors(
            $"{SectionName}:{nameof(this.Https)}",
            QuicListener.IsSupported));

        return errors;
    }

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
        if (unknownMethods != McpTransportAuthenticationMethods.None)
        {
            yield return $"{SectionName}:{nameof(this.Authentication)} — '{(int)unknownMethods}' names no authentication method; write '{nameof(McpTransportAuthenticationMethods.ApiKey)}', '{nameof(McpTransportAuthenticationMethods.OAuth)}', both separated by a comma, or '{nameof(McpTransportAuthenticationMethods.None)}'.";

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
            yield return $"{SectionName}:{nameof(this.ApiKeys)} — '{nameof(McpTransportAuthenticationMethods.ApiKey)}' authentication is selected and no key is configured, so no client could authenticate with one.";
        }

        // Configured-but-unchecked is refused rather than ignored in both directions below, because settings nothing
        // reads are a deployment believing it is protected — which is worse than one that knows it is not.
        if (!this.AllowsApiKey && this.ApiKeys.Count > 0)
        {
            yield return $"{SectionName}:{nameof(this.ApiKeys)} — API keys are configured while '{nameof(McpTransportAuthenticationMethods.ApiKey)}' is not among the authentication methods, so none of them is checked; add it or remove them.";
        }
    }

    private IEnumerable<string> FindOAuthErrors()
    {
        if (!this.AllowsOAuth)
        {
            if (this.OAuth.IsConfigured)
            {
                yield return $"{SectionName}:{nameof(this.OAuth)} — authorization servers are configured while '{nameof(McpTransportAuthenticationMethods.OAuth)}' is not among the authentication methods, so no token is checked against them; add it or remove the section.";
            }

            yield break;
        }

        foreach (var error in this.OAuth.FindConfigurationErrors())
        {
            yield return $"{SectionName}:{nameof(this.OAuth)}:{error}";
        }
    }
}
