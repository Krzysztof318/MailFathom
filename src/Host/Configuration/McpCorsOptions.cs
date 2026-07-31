// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Infrastructure.Security;

namespace MailMcp.Host.Configuration;

/// <summary>Which browser origins the MCP endpoint answers, and what it tells a browser it may read.</summary>
/// <remarks>
/// <para>
/// Allowing every origin is the default because the endpoint is not protected by who is calling it — it is protected by
/// the credential the caller presents. Narrowing the origins is worth doing where a browser-hosted client is the only
/// intended consumer, and it is the control the MCP transport specification asks for against DNS rebinding, but it
/// authenticates nothing on its own and is never the reason a request is trusted.
/// </para>
/// <para>
/// The two settings are alternatives rather than layers. An operator who lists origins while leaving
/// <see cref="AllowAnyOrigin" /> on has stated two policies, and guessing which one they meant would either widen a
/// deployment they narrowed or narrow one they widened; that combination is refused at startup instead.
/// </para>
/// <para>
/// Turning <see cref="AllowAnyOrigin" /> off and listing nothing is the third posture rather than a mistake: no browser
/// origin is served at all. It is what a deployment whose clients are not browsers wants, since such a client sends no
/// <c>Origin</c> and is served regardless, and it is the only posture that closes DNS rebinding for an endpoint running
/// under <see cref="McpTransportAuthenticationMode.None" />.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class McpCorsOptions
{
    /// <summary>Gets or sets whether every browser origin is served.</summary>
    /// <remarks>Credentials are never enabled alongside it, so a browser may read a response it obtained with an explicit bearer credential and may never attach an ambient cookie to the request that produced it.</remarks>
    public bool AllowAnyOrigin { get; set; } = true;

    /// <summary>Gets the exact origins served when <see cref="AllowAnyOrigin" /> is off, for example <c>https://client.example.test</c>.</summary>
    /// <remarks>Left empty with <see cref="AllowAnyOrigin" /> off, no browser origin is served and only clients that send no <c>Origin</c> reach the endpoint.</remarks>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Finds everything an operator must fix before this policy can be applied.</summary>
    /// <returns>One message per faulty setting, relative to this section, empty when the policy is usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        var errors = new List<string>();

        if (this.AllowAnyOrigin && this.AllowedOrigins.Count > 0)
        {
            errors.Add($"{nameof(this.AllowAnyOrigin)} — every origin is served and an exact origin list is configured; state one policy or the other.");
        }

        errors.AddRange(this.FindAllowedOriginErrors());

        return errors;
    }

    /// <summary>Maps the configured settings onto the policy the endpoint judges a request's origin by.</summary>
    /// <returns>The origin policy.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public McpOriginPolicy ToOriginPolicy()
    {
        if (this.AllowAnyOrigin)
        {
            return McpOriginPolicy.AllowingAnyOrigin;
        }

        if (this.AllowedOrigins.Count == 0)
        {
            return McpOriginPolicy.RefusingEveryBrowserOrigin;
        }

        var normalizedOrigins = this.NormalizedAllowedOrigins().ToArray();

        return normalizedOrigins.Length == this.AllowedOrigins.Count
            ? McpOriginPolicy.Restricting(normalizedOrigins)
            : throw new InvalidOperationException(
                "The configured origins were mapped before they were validated, so at least one of them is unusable.");
    }

    /// <summary>Reports the configured origins that cannot be compared against what a browser sends.</summary>
    /// <remarks>
    /// Duplicates are reported after normalization rather than before, because two spellings of one origin are one
    /// entry to every browser and an operator who listed both has said something about their intent that the accepted
    /// list would silently discard.
    /// </remarks>
    private IEnumerable<string> FindAllowedOriginErrors()
    {
        var claimedOrigins = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, configuredOrigin) in this.AllowedOrigins.Index())
        {
            var settingPath = $"{nameof(this.AllowedOrigins)}:{index}";

            if (!McpOriginPolicy.TryNormalize(configuredOrigin, out var normalizedOrigin))
            {
                yield return $"{settingPath} — '{configuredOrigin}' is not an origin; write a scheme, a host, and a port where the port is not the scheme's default, and nothing else.";
            }
            else if (!claimedOrigins.Add(normalizedOrigin))
            {
                yield return $"{settingPath} — '{configuredOrigin}' repeats an origin the list already carries.";
            }
        }
    }

    private IEnumerable<string> NormalizedAllowedOrigins() => this.AllowedOrigins
        .Select(configuredOrigin => McpOriginPolicy.TryNormalize(configuredOrigin, out var normalizedOrigin)
            ? normalizedOrigin
            : null)
        .OfType<string>();
}
