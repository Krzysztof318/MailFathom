// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Proves that a deployment upgrading over a retired credential key reads about that key and nothing else.</summary>
/// <remarks>
/// The message itself belongs to the section that composes it and is covered there. What only this composition can
/// settle is which refusal an operator meets: whether any surface is served at all is judged before any section answers
/// for itself, so a section refused for a retired key has to go on reporting that it is enabled or the start stops on a
/// sentence about a process serving nothing — which is a statement about their configuration that is false, and which
/// says nothing about the credential that replaced the setting.
/// </remarks>
public sealed class ComposedSettingsRetiredSettingsTests
{
    [Theory]
    [InlineData(McpEndpointOptions.SectionName, "ApiKey:Name")]
    [InlineData(ClientEndpointOptions.SectionName, "PublicKey:Name")]
    public void FindSurfaceRefusals_TheOnlyEnabledSurfaceCarryingARetiredKey_IsRefusedForTheKeyRatherThanForServingNothing(
        string sectionName,
        string retiredSetting)
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{sectionName}:Enabled"] = "true",
                [$"{sectionName}:Authentication:0:{retiredSetting}"] = "workstation",
                ["HealthEndpoint:Enabled"] = "false",
            })
            .Build();

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals, candidate => candidate.SectionName == sectionName);
        Assert.All(refusal.Errors, error => Assert.Contains("mfctl", error, StringComparison.Ordinal));
    }
}
