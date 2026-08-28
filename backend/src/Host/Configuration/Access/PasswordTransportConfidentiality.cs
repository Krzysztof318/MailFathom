// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;

namespace MailFathom.Host.Configuration.Access;

/// <summary>Refuses a surface that would accept a password over a hop this deployment has not established as encrypted.</summary>
/// <remarks>
/// <para>
/// Every other credential this deployment accepts is a value a machine holds: a key, an assertion, or a token, each
/// issued to one client, each replaceable without anybody being told, and none of them reused anywhere else. A password
/// is none of those things. It is typed by a person, it is the credential that person is most likely to have typed
/// somewhere else, and reading it once off the network is reading it for as long as it stands. So a clear-text hop —
/// which the other methods are warned about and permitted on, because a deployment may knowingly be running on a
/// loopback socket or behind a proxy — is refused outright for this one.
/// </para>
/// <para>
/// Two arrangements satisfy it, and they are the two a deployment actually runs. The endpoint terminates TLS itself,
/// which is this process holding the certificate and needing nothing else stated. Or the operator has named what stands
/// in front, through <see cref="ReverseProxyOptions.TrustedProxies" />, which is the existing contract by which this
/// process believes a forwarded scheme at all: the proxy terminates TLS for the client, the hop from it to here is
/// inside a network the operator controls, and naming the proxy is what says so. Nothing new is invented for passwords
/// — the same section that already decides whether a forwarded <c>https</c> is believed is what decides this.
/// </para>
/// <para>
/// A section naming a range that covers every address is not that contract. It trusts whatever can open a connection,
/// which is the posture a section naming nothing already has, so it says nothing about what stands in front and cannot
/// be read as a promise that anything does. <see cref="Hosting.Warnings.ReverseProxyTrustWarning" /> already reports
/// that range as the giving-up it is; here it is a refusal rather than a warning, because a password is what would
/// cross it.
/// </para>
/// <para>
/// What this decides is which deployments may accept a password at all, and it is read from what the endpoint's
/// transport mode actually serves rather than from whether TLS is configured: the mode that binds both sockets serves
/// its routes on the clear-text one too, unless that socket redirects away from them, so a certificate present beside
/// a redirect left off is exactly the arrangement this refuses.
/// </para>
/// <para>
/// <see cref="Security.Basic.BasicAuthenticationHandler" /> then refuses per request as well, and the two are not one
/// check written twice. This one cannot see a request; the second arrangement it permits — a clear-text listener behind
/// a named proxy — leaves that socket open to anything that can route to it, and a request arriving there from
/// anywhere but the proxy carries no forwarded scheme. That is the request the handler refuses, before the header is
/// read.
/// </para>
/// </remarks>
internal static class PasswordTransportConfidentiality
{
    /// <summary>Finds what an operator must fix before a surface may accept a username and password.</summary>
    /// <param name="sectionName">The endpoint's configuration section, which every message names its settings under.</param>
    /// <param name="enabled">Whether the endpoint is served at all.</param>
    /// <param name="allowsBasic">Whether one of the endpoint's configured methods is a password.</param>
    /// <param name="servesClearText">Whether the endpoint answers its routes on a socket nothing encrypts, which the mode that binds both sockets does too unless the clear-text one redirects.</param>
    /// <param name="reverseProxy">The reverse-proxy settings, which say whether the operator has named what stands in front.</param>
    /// <returns>One message when the arrangement is refused, empty when it is not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sectionName" /> or <paramref name="reverseProxy" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="reverseProxy" /> has not passed <see cref="ReverseProxyOptions.FindConfigurationErrors" />, so read it after that section has answered.</exception>
    internal static IReadOnlyList<string> FindConfigurationErrors(
        string sectionName,
        bool enabled,
        bool allowsBasic,
        bool servesClearText,
        ReverseProxyOptions reverseProxy)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(reverseProxy);

        if (!enabled || !allowsBasic || !servesClearText || EstablishesATrustedProxyContract(reverseProxy))
        {
            return [];
        }

        return
        [
            $"{sectionName}:{nameof(McpEndpointOptions.Authentication)} — a '{nameof(TransportAuthenticationOptions.Basic)}' method "
            + "accepts a password typed by a person, and this endpoint answers its routes on a socket nothing encrypts and "
            + "is not declared to sit behind a proxy that terminates TLS, so the password would cross a hop anything on the "
            + $"network path can read. Either set '{sectionName}:Transport' to 'HttpsOnly' and configure "
            + $"'{sectionName}:Https:Endpoints' so this process presents your domain's certificate — 'HttpAndHttps' does "
            + $"too, but only with '{sectionName}:Https:Redirect:Enabled' left on, because the clear-text socket answers "
            + "the routes rather than redirecting away from them otherwise — or name the proxy that terminates TLS in "
            + $"'{ReverseProxyOptions.SectionName}:{nameof(ReverseProxyOptions.TrustedProxies)}' — an address such as "
            + "'10.0.0.5' or a network such as '10.0.0.0/24', never a range covering every address, which trusts whatever "
            + "can connect and therefore states nothing about what stands in front.",
        ];
    }

    /// <summary>Reports whether the operator has said that a TLS-terminating proxy stands in front of this process.</summary>
    /// <remarks>Naming a proxy is the whole of it, and naming a range covering every address is not naming one: that range is what a section stating nothing already resolves to, so accepting it would make the refusal above satisfiable by writing <c>0.0.0.0/0</c>.</remarks>
    private static bool EstablishesATrustedProxyContract(ReverseProxyOptions reverseProxy) =>
        reverseProxy.NamesAProxy && reverseProxy.ToTrustedProxyRangesCoveringEveryAddress().Count == 0;
}
