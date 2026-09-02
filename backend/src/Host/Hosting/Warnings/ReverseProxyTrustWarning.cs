// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup that the proxy trust in force covers every address rather than a proxy.</summary>
/// <remarks>
/// <para>
/// A range covering every address is accepted rather than refused, because an operator can mean it: a load balancer
/// pool with no stable address, or a network already closed by something other than this setting. It is also what a
/// deployment that configured nothing runs on, which is the case this warning matters most for — nobody chose that
/// posture, so nobody would otherwise learn they had it.
/// </para>
/// <para>
/// What makes it worth a line of its own is that it does not merely widen who is believed. The refusal of an access
/// token that arrived without transport encryption decides by reading <c>HttpContext.Request.IsHttps</c>, which is the
/// scheme the forwarded-header policy has already rewritten, so with every peer trusted any client can assert that its
/// own hop was encrypted and have a reusable credential accepted over clear text. That is a protection the deployment
/// gave up, and giving it up silently is what this exists to prevent.
/// </para>
/// <para>
/// The two postures are reported separately because the remedy differs: one deployment has to narrow a range it wrote,
/// the other has to name a proxy it never named. Only the wording turns on that; what was given up is identical, and
/// both lines say so.
/// </para>
/// <para>
/// Nothing else is reported. A merely wide range — a private <c>/8</c> — is a judgement about somebody's network
/// rather than a fact about it, and a warning that fired on one would be a line an operator learns to scroll past.
/// This is said once, while the host starts, and never per request, for the same reason: a line repeated at request
/// rate is a line nobody reads.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ReverseProxyTrustWarning : IHostedService
{
    private readonly ReverseProxyOptions reverseProxySettings;
    private readonly ILogger<ReverseProxyTrustWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="reverseProxySettings">The reverse-proxy settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reverseProxySettings" /> is <see langword="null" />.</exception>
    public ReverseProxyTrustWarning(
        IOptions<ReverseProxyOptions> reverseProxySettings,
        ILogger<ReverseProxyTrustWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(reverseProxySettings);

        this.reverseProxySettings = reverseProxySettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var rangesCoveringEveryAddress = this.reverseProxySettings.ToTrustedProxyRangesCoveringEveryAddress();

        if (rangesCoveringEveryAddress.Count == 0)
        {
            return Task.CompletedTask;
        }

        var trustedRanges = string.Join(", ", rangesCoveringEveryAddress);

        if (this.reverseProxySettings.NamesAProxy)
        {
            this.LogConfiguredRangeTrustsEveryPeer(trustedRanges);
        }
        else
        {
            this.LogUnconfiguredSectionTrustsEveryPeer(trustedRanges);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "ReverseProxy:TrustedProxies names {TrustedRanges}, which covers every address, so a forwarded scheme "
            + "and host are read from any peer that can open a connection rather than from a proxy. This also turns off "
            + "the refusal of an access token that arrived without transport encryption, because that refusal reads the "
            + "scheme a forwarded header set — so a client can claim its own hop was encrypted and have the token "
            + "accepted over clear text. Narrow the range to the addresses your proxies actually use unless something "
            + "other than this setting already closes the network.")]
    private partial void LogConfiguredRangeTrustsEveryPeer(string trustedRanges);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "ReverseProxy:TrustedProxies names no proxy, so this process trusts {TrustedRanges} — a forwarded "
            + "scheme and host are read from any peer that can open a connection. This also turns off the refusal of an "
            + "access token that arrived without transport encryption, because that refusal reads the scheme a "
            + "forwarded header set, so a client can claim its own hop was encrypted and have the token accepted over "
            + "clear text. Name the addresses or CIDR networks your proxies actually use, for example '10.0.0.5' or "
            + "'10.0.0.0/24', to read a forwarded header from them alone; write the ranges above explicitly if trusting "
            + "every peer is what this deployment means.")]
    private partial void LogUnconfiguredSectionTrustsEveryPeer(string trustedRanges);
}
