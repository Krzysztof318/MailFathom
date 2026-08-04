// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup that the configured proxy trust covers every address rather than a proxy.</summary>
/// <remarks>
/// <para>
/// A range covering every address is accepted rather than refused, because an operator can mean it: a load balancer
/// pool with no stable address, or a network already closed by something other than this setting. It is reported for
/// the reason every other posture here is — only an operator knows which of those they have, and none of it is
/// something MailFathom can detect.
/// </para>
/// <para>
/// What makes it worth a line of its own is that it does not merely widen who is believed. The refusal of an access
/// token that arrived without transport encryption decides by reading <c>HttpContext.Request.IsHttps</c>, which is the
/// scheme this mode has already rewritten, so with every peer trusted any client can assert that its own hop was
/// encrypted and have a reusable credential accepted over clear text. That is a protection the deployment gave up, and
/// giving it up silently is what this exists to prevent.
/// </para>
/// <para>
/// Only a prefix covering every address is reported. A merely wide range — a private <c>/8</c> — is a judgement about
/// somebody's network rather than a fact about it, and a warning that fired on one would be a line an operator learns
/// to scroll past.
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
        if (!this.reverseProxySettings.Enabled)
        {
            return Task.CompletedTask;
        }

        var rangesCoveringEveryAddress = this.reverseProxySettings.ToTrustedProxyRangesCoveringEveryAddress();

        if (rangesCoveringEveryAddress.Count > 0)
        {
            this.LogEveryPeerTrusted(string.Join(", ", rangesCoveringEveryAddress));
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
    private partial void LogEveryPeerTrusted(string trustedRanges);
}
