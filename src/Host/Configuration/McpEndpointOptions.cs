// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
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
/// The endpoint is disabled by default, so a deployment that configures nothing serves no mailbox over the network.
/// Enabling it requires naming an <see cref="Authentication" /> mode, so the unauthenticated posture is something an
/// operator wrote down rather than something a missing setting selected for them.
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

    /// <summary>Gets or sets whether the MCP endpoint is served at all.</summary>
    /// <remarks>Disabled unless a deployment states otherwise, so reaching a mailbox over MCP is always something an operator turned on.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets what a client must present before a request is served, which an enabled endpoint must name.</summary>
    /// <remarks>Nullable rather than defaulted, because every candidate default is a posture, and the one that would be safe to assume is also the one that refuses every client a working deployment has.</remarks>
    public McpTransportAuthenticationMode? Authentication { get; set; }

    /// <summary>Gets the API keys a client may authenticate with, each a named secret with its own lifetime.</summary>
    /// <remarks>
    /// Several entries rather than one, so a key can be replaced by adding its successor, moving clients across, and
    /// removing the old entry — with both valid in between and no window in which nothing authenticates. An expired
    /// entry may stay in the list; it authenticates nothing and documents what was retired.
    /// </remarks>
    public IList<ConfiguredSecret> ApiKeys { get; } = [];

    /// <summary>Gets or sets which browser origins the endpoint answers.</summary>
    public McpCorsOptions Cors { get; set; } = new();

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
        if (this.Authentication is not { } authenticationMode)
        {
            yield return $"{SectionName}:{nameof(this.Authentication)} — an enabled endpoint must state '{nameof(McpTransportAuthenticationMode.ApiKey)}' or '{nameof(McpTransportAuthenticationMode.None)}'; there is no default, because an unauthenticated endpoint serves every synchronized mailbox to anything that can reach it.";

            yield break;
        }

        // The binder accepts any number for an enum, so 'Authentication=2' would bind to a value no member declares.
        // Every check below asks whether the mode equals one of the two, and such a value answers no to all of them: it
        // registers no authentication, requires no credential, and leaves the unauthenticated warning silent because it
        // is not None either. Refusing it here is what keeps a typo from opening the endpoint instead of closing it.
        if (!Enum.IsDefined(authenticationMode))
        {
            yield return $"{SectionName}:{nameof(this.Authentication)} — '{(int)authenticationMode}' names no authentication mode; state '{nameof(McpTransportAuthenticationMode.ApiKey)}' or '{nameof(McpTransportAuthenticationMode.None)}'.";

            yield break;
        }

        if (authenticationMode == McpTransportAuthenticationMode.ApiKey && this.ApiKeys.Count == 0)
        {
            yield return $"{SectionName}:{nameof(this.ApiKeys)} — '{nameof(McpTransportAuthenticationMode.ApiKey)}' authentication is selected and no key is configured, so no client could authenticate.";
        }

        if (authenticationMode == McpTransportAuthenticationMode.None && this.ApiKeys.Count > 0)
        {
            yield return $"{SectionName}:{nameof(this.ApiKeys)} — API keys are configured while authentication is '{nameof(McpTransportAuthenticationMode.None)}', so none of them is checked; select '{nameof(McpTransportAuthenticationMode.ApiKey)}' or remove them.";
        }
    }
}
