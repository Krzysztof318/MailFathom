// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers which listeners exist only to redirect, decided from the socket a connection arrived on.</summary>
/// <remarks>
/// The first question is what makes the clear-text listener serve nothing: a port this answers yes for never reaches a
/// route, and a port it answers no for is untouched. Getting it wrong in the second direction would put every request the
/// process serves behind a redirect, so the negative cases here carry as much weight as the positive ones.
/// </remarks>
public sealed class ClearTextRedirectTargetsTests
{
    private static readonly ClearTextRedirectTargets Targets = new(
    [
        new ClearTextRedirectListener(
            8080,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["mail.example.test"] = 8443 }),
    ]);

    [Fact]
    public void RedirectsOnly_TheClearTextListener_ServesNothingButTheRedirect() =>
        Assert.True(Targets.RedirectsOnly(8080));

    /// <summary>The listener the profiles themselves bind carries the surface, so nothing may intercept it.</summary>
    [Theory]
    [InlineData(8443)]
    [InlineData(8081)]
    [InlineData(8090)]
    public void RedirectsOnly_AnyOtherListener_IsLeftAlone(int localPort) =>
        Assert.False(Targets.RedirectsOnly(localPort));

    [Fact]
    public void RedirectsOnly_ADeploymentWhereNoSurfaceRedirects_LeavesEveryListenerAlone() =>
        Assert.False(new ClearTextRedirectTargets([]).RedirectsOnly(8080));

    [Fact]
    public void PublishedHttpsPortFor_APublishedDomain_ReportsTheProfilePort() =>
        Assert.Equal(8443, Targets.PublishedHttpsPortFor(8080, "mail.example.test"));

    [Fact]
    public void PublishedHttpsPortFor_ADomainTheSurfaceDoesNotPublish_ReportsNothing() =>
        Assert.Null(Targets.PublishedHttpsPortFor(8080, "elsewhere.test"));

    /// <summary>A published domain reached through a listener that does not redirect is not a redirect either.</summary>
    [Fact]
    public void PublishedHttpsPortFor_APublishedDomainOnAListenerThatServesRoutes_ReportsNothing() =>
        Assert.Null(Targets.PublishedHttpsPortFor(8443, "mail.example.test"));

    /// <summary>The endpoint sections refuse two listeners on one port, so reaching this state is a composition defect rather than a configuration one.</summary>
    [Fact]
    public void Constructor_TwoListenersClaimingOnePort_Throws() =>
        Assert.Throws<ArgumentException>(() => new ClearTextRedirectTargets(
        [
            new ClearTextRedirectListener(8080, new Dictionary<string, int> { ["one.example.test"] = 8443 }),
            new ClearTextRedirectListener(8080, new Dictionary<string, int> { ["two.example.test"] = 9443 }),
        ]));
}
