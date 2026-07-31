// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

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
/// One setting carries the whole policy. A pair of them — an allow-any switch beside a list — states one decision twice,
/// and half of its combinations are startup errors for exactly that reason: the pair can say two things at once and
/// guessing which was meant would either widen a deployment an operator narrowed or narrow one they widened.
/// <see cref="AnyOriginValue" /> written in the list says the same thing and cannot contradict itself.
/// </para>
/// <para>
/// The three postures are consequences of the list rather than settings of their own: <see cref="AnyOriginValue" /> alone
/// serves every browser origin, a list of origins serves exactly those, and an empty list serves no browser at all —
/// which still serves every client that sends no <c>Origin</c>, meaning every non-browser client.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class McpCorsOptions
{
    /// <summary>The entry that stands for every browser origin rather than for one an operator could have written.</summary>
    public const string AnyOriginValue = "*";

    /// <summary>Gets the browser origins served, for example <c>https://client.example.test</c>, or <see cref="AnyOriginValue" /> for all of them.</summary>
    /// <remarks>
    /// It reads as <see cref="AnyOriginValue" /> when a deployment configures no list at all, which
    /// <see cref="McpEndpointOptions.ReadFrom" /> applies, because the binder cannot tell an absent list from an empty
    /// one and the two mean opposite things here. The property's own default is therefore the empty list rather than
    /// the permissive one: the permissive default belongs to the configured section, and an object built any other way
    /// starts from the posture that serves no browser rather than from the one that serves every page on the internet.
    /// </remarks>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Gets whether every browser origin is served, which is the posture the startup warning reports under an unauthenticated endpoint.</summary>
    public bool ServesEveryBrowserOrigin => this.AllowedOrigins.Contains(AnyOriginValue, StringComparer.Ordinal);

    /// <summary>Serves every browser origin, which is what a deployment that configured no list gets.</summary>
    /// <remarks>Called once while the section is read, never after the policy has been derived from the list.</remarks>
    public void ServeEveryBrowserOrigin() => this.AllowedOrigins.Add(AnyOriginValue);

    /// <summary>Finds everything an operator must fix before this policy can be applied.</summary>
    /// <returns>One message per faulty setting, relative to this section, empty when the policy is usable.</returns>
    /// <remarks>
    /// <see cref="AnyOriginValue" /> is answered before the entries are read as origins, because it is not one and no
    /// operator could configure a real origin that collides with it. What is left to refuse is the list that carries it
    /// beside something else, which states two policies for the same reason the removed allow-any switch could.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (this.ServesEveryBrowserOrigin)
        {
            return this.AllowedOrigins.Count > 1
                ? [$"{nameof(this.AllowedOrigins)} — '{AnyOriginValue}' is listed beside another entry, which states two policies at once; list the origins served, or '{AnyOriginValue}' on its own."]
                : [];
        }

        return [.. this.FindAllowedOriginErrors()];
    }

    /// <summary>Maps the configured list onto the policy the endpoint judges a request's origin by.</summary>
    /// <returns>The origin policy.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public McpOriginPolicy ToOriginPolicy()
    {
        if (this.ServesEveryBrowserOrigin)
        {
            return McpOriginPolicy.AllowingAnyOrigin;
        }

        if (this.AllowedOrigins.Count == 0)
        {
            return McpOriginPolicy.ServingNoBrowserOrigin;
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
    /// list would silently discard. <see cref="AnyOriginValue" /> never reaches this: it is not an origin any
    /// normalization accepts, and a list carrying it beside anything else was already refused.
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
