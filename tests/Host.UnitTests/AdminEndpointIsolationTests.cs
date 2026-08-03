// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the rule that gives the administrative endpoint its own port rather than an extra one.</summary>
/// <remarks>
/// Routing matches a path, not a socket. Without this the administrative routes would answer on every listener the
/// process opened, and configuring a separate port would change where the surface *also* answers rather than where it
/// answers — which is the opposite of the control an operator configures one to get.
/// </remarks>
public sealed class AdminEndpointIsolationTests
{
    private static readonly IReadOnlySet<int> AdminListener = new HashSet<int> { 8090 };

    [Fact]
    public void ListenerServesPath_AnAdministrativeRequestOnTheAdministrativeListener_IsServed() =>
        Assert.True(AdminEndpointIsolation.ListenerServesPath(8090, "/api/admin/session", AdminListener));

    /// <summary>The whole point of a separate port: the administrative surface is not reachable through the MCP one.</summary>
    [Fact]
    public void ListenerServesPath_AnAdministrativeRequestOnAnotherListener_IsRefused() =>
        Assert.False(AdminEndpointIsolation.ListenerServesPath(8080, "/api/admin/session", AdminListener));

    /// <summary>And the reverse, which matters as much: the administrative listener is not a second way into the mailbox.</summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/health")]
    [InlineData("/")]
    public void ListenerServesPath_AnythingElseOnTheAdministrativeListener_IsRefused(string path) =>
        Assert.False(AdminEndpointIsolation.ListenerServesPath(8090, path, AdminListener));

    [Fact]
    public void IsAdminPath_APathThatMerelyStartsWithTheSameLetters_IsNotAnAdministrativePath() =>
        Assert.False(AdminEndpointIsolation.IsAdminPath("/api/administration-console"));

    [Fact]
    public void IsAdminPath_TheApiPrefixWithoutTheAdminSegment_IsNotAnAdministrativePath() =>
        Assert.False(AdminEndpointIsolation.IsAdminPath("/api/something-else"));

    [Theory]
    [InlineData("/api/admin")]
    [InlineData("/api/admin/")]
    [InlineData("/API/Admin/session")]
    public void IsAdminPath_TheRoutePrefixHoweverItIsSpelled_IsAnAdministrativePath(string path) =>
        Assert.True(AdminEndpointIsolation.IsAdminPath(new PathString(path)));
}
