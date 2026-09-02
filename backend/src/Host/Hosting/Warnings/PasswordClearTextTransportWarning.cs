// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup that a surface accepting a password answers its routes on a socket nothing encrypts.</summary>
/// <remarks>
/// <para>
/// A password is the credential a person types, and reading one off the network is reading it for as long as it stands
/// — so a clear-text hop under one is worth saying at every startup. What it is not is grounds to refuse the
/// deployment. This process can read the scheme of its own socket and nothing beyond it: a deployment publishing on
/// loopback for a proxy that need not be declared, and one exposing the same socket to a network nobody meant to reach
/// it, are indistinguishable from here. A refusal written from that reading refused the first as readily as the second,
/// which is why it was withdrawn and this reports instead.
/// </para>
/// <para>
/// That is what an API key crossing the same hop already gets. <see cref="McpTransportEncryptionWarning" /> says one
/// is readable, <see cref="ClientTransportSecurityWarning" /> says the same of the client's, and neither refuses.
/// Configuring TLS, or putting something in front that terminates it, is the administrator's decision about their own
/// deployment. It is not what every credential gets: an OAuth bearer token arriving over a clear-text hop is still
/// refused per request, silently and without being read, by the guard in
/// <c>TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption</c>. That refusal is a different
/// judgement about a different credential — a token names an authorization server this deployment did not issue it
/// against — and nothing here withdraws it.
/// </para>
/// <para>
/// A named reverse proxy is read the way <see cref="McpTransportEncryptionWarning" /> reads one, and for the same
/// reason: a <see cref="ReverseProxyOptions" /> section naming one is the operator saying what stands in front, so the
/// message then describes the hop between that proxy and this process rather than a hop to the client. Nothing is
/// silenced by it — that hop is real and the password crosses it — and the section is read once here rather than
/// interpreted a second way, because one reading of what stands in front is what keeps two warnings about the same
/// socket saying the same thing.
/// </para>
/// <para>
/// The administrative endpoint is not read at all. It refuses a <c>Basic</c> entry outright, for a reason that has
/// nothing to do with transport — that surface answers for the deployment rather than for a person — so it has no
/// arrangement this warning could describe.
/// </para>
/// <para>
/// One hosted service reads both request-serving surfaces rather than one per surface, because what it reports is a
/// property of the socket and the credential rather than of what is served on it. Each enabled surface still gets a
/// record of its own, reported against the section its <c>Basic</c> entry was written in, including where the two
/// share one socket: what an operator acts on is the section they would edit, and a shared port is not a reason to
/// tell them about only one of the two. It is registered whether or not either surface is enabled, because it is the
/// warning that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class PasswordClearTextTransportWarning : IHostedService
{
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly ClientEndpointOptions clientEndpointSettings;
    private readonly ReverseProxyOptions reverseProxySettings;
    private readonly ILogger<PasswordClearTextTransportWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="clientEndpointSettings">The client endpoint settings startup was composed from.</param>
    /// <param name="reverseProxySettings">The reverse-proxy settings startup was composed from, which say whether the operator has named what stands in front.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public PasswordClearTextTransportWarning(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<ClientEndpointOptions> clientEndpointSettings,
        IOptions<ReverseProxyOptions> reverseProxySettings,
        ILogger<PasswordClearTextTransportWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(clientEndpointSettings);
        ArgumentNullException.ThrowIfNull(reverseProxySettings);

        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.clientEndpointSettings = clientEndpointSettings.Value;
        this.reverseProxySettings = reverseProxySettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.mcpEndpointSettings is { Enabled: true, AllowsBasic: true, ServesClearText: true })
        {
            this.WarnAbout(McpEndpointOptions.SectionName, this.mcpEndpointSettings.Port);
        }

        if (this.clientEndpointSettings is { Enabled: true, AllowsBasic: true, ServesClearText: true })
        {
            this.WarnAbout(ClientEndpointOptions.SectionName, this.clientEndpointSettings.Port);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reports one surface's hop, in whichever of the two postures the proxy section says this deployment runs.</summary>
    private void WarnAbout(string sectionName, int clearTextPort)
    {
        if (this.reverseProxySettings.NamesAProxy)
        {
            this.LogPasswordCrossesTheHopBehindATrustedReverseProxy(
                sectionName,
                clearTextPort,
                this.reverseProxySettings.TrustedProxies.Count);
        }
        else
        {
            this.LogPasswordCrossesAClearTextHop(sectionName, clearTextPort);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{EndpointSectionName} accepts an owner's username and password and answers its routes on port "
            + "{ClearTextPort}, which nothing encrypts, so every password signed in with crosses that hop readable by "
            + "anything on the network path — and a password is the one credential here that a person typed and may "
            + "have typed elsewhere. This is the expected posture on a loopback bind and behind a TLS-terminating "
            + "reverse proxy; anywhere else, set {EndpointSectionName}:Transport to 'HttpsOnly' and configure "
            + "{EndpointSectionName}:Https:Endpoints so this process presents your domain's certificate, or name the "
            + "proxy that terminates TLS in ReverseProxy:TrustedProxies.")]
    private partial void LogPasswordCrossesAClearTextHop(string endpointSectionName, int clearTextPort);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{EndpointSectionName} accepts an owner's username and password behind the {TrustedProxyCount} "
            + "trusted reverse proxy source(s) ReverseProxy:TrustedProxies names, so the hop this process serves on "
            + "port {ClearTextPort} is the one between that proxy and here and TLS to your clients is the proxy's to "
            + "terminate. Keep that hop inside a network you control: on it, every password signed in with is readable "
            + "by anything on the path.")]
    private partial void LogPasswordCrossesTheHopBehindATrustedReverseProxy(
        string endpointSectionName,
        int clearTextPort,
        int trustedProxyCount);
}
