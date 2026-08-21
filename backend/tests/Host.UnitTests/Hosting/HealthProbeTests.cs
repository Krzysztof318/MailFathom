// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers the three questions an orchestrator asks and how each one selects the checks that answer it.</summary>
/// <remarks>
/// Both the path and the tag are published identities — a deployment writes the path into a probe specification and a
/// registration writes the tag — so what is asserted here is the identities themselves rather than a lookup that could
/// be renamed and re-derived without anything failing.
/// </remarks>
public sealed class HealthProbeTests
{
    [Fact]
    public void All_TheProbesTheListenerServes_AnswerThreeDistinctQuestionsOnThreeDistinctPaths()
    {
        // Arrange
        var probes = HealthProbe.All;

        // Act
        var paths = probes.Select(probe => probe.Path).ToArray();
        var tags = probes.Select(probe => probe.Tag).ToArray();

        // Assert
        Assert.Equal(["/started", "/health", "/alive"], paths);
        Assert.Equal(["startup", "ready", "live"], tags);
    }

    [Theory]
    [InlineData("/started", true)]
    [InlineData("/health", true)]
    [InlineData("/alive", true)]
    [InlineData("/HEALTH", true)]
    // Routing ignores a trailing slash, so a probe endpoint answers these. Reading them as application paths is what
    // would let the readiness answer past the listener isolation and onto the port MCP clients reach.
    [InlineData("/health/", true)]
    [InlineData("/alive/", true)]
    [InlineData("/started/", true)]
    [InlineData("/HEALTH/", true)]
    [InlineData("/mcp", false)]
    [InlineData("/", false)]
    public void IsProbePath_APath_ReportsWhetherAProbeAnswersIt(string path, bool expected)
    {
        // Arrange
        var requestPath = new PathString(path);

        // Act
        var isProbePath = HealthProbe.IsProbePath(requestPath);

        // Assert
        Assert.Equal(expected, isProbePath);
    }

    /// <summary>
    /// A probe answers one path. Treating everything beneath it as a probe path would keep requests off the application
    /// listener that no probe was ever going to answer, which is a silent way to lose a route.
    /// </summary>
    [Theory]
    [InlineData("/health/details")]
    [InlineData("/healthz")]
    public void IsProbePath_APathNoProbeAnswers_IsNotAProbePath(string path)
    {
        // Arrange
        var requestPath = new PathString(path);

        // Act
        var isProbePath = HealthProbe.IsProbePath(requestPath);

        // Assert
        Assert.False(isProbePath);
    }

    /// <summary>
    /// The set is closed, so the struct default is the one value that is not a probe. C# gives no way to forbid it, so
    /// it reports itself instead of answering for a path or a tag it does not have.
    /// </summary>
    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfAndRefusesToAnswer()
    {
        // Arrange
        var unspecified = default(HealthProbe);

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Path);
        Assert.Throws<InvalidOperationException>(() => unspecified.Tag);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    [Fact]
    public void IsSpecified_EveryDeclaredProbe_NamesAPathAndATag()
    {
        // Act, Assert
        Assert.All(HealthProbe.All, probe => Assert.True(probe.IsSpecified));
        Assert.Equal(HealthProbe.All.Select(probe => probe.Path), HealthProbe.All.Select(probe => probe.ToString()));
    }

    [Fact]
    public void Selects_ARegistration_ReadsTheProbeMembershipItStated()
    {
        // Arrange
        var readinessCheck = StubHealthCheck.Registration("database", HealthStatus.Healthy, HealthProbe.Readiness.Tag);
        var untaggedCheck = StubHealthCheck.Registration("unclassified", HealthStatus.Healthy);

        // Act
        var selected = HealthProbe.All.Where(probe => probe.Selects(readinessCheck)).ToArray();
        var selectedForUntagged = HealthProbe.All.Where(probe => probe.Selects(untaggedCheck)).ToArray();

        // Assert
        Assert.Equal([HealthProbe.Readiness], selected);
        Assert.Empty(selectedForUntagged);
    }
}
