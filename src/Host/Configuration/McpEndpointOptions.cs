// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Infrastructure.Secrets;

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

        return errors;
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
