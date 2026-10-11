// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

/// <summary>
/// Covers the one client preference a settings policy says anything about: whether the policies governing a person
/// force their telemetry switch, to what, and which of the two scopes is the one holding it.
/// </summary>
public sealed class ForcedClientTelemetryTests
{
    private const string ForcingOn = """{"Users":{"Forced":{"ClientPreferences":{"TelemetryEnabled":true}}}}""";
    private const string ForcingOff = """{"Users":{"Forced":{"ClientPreferences":{"TelemetryEnabled":false}}}}""";
    private const string ForcingSomethingElse = """{"Users":{"Forced":{"Language":"Polish"}}}""";

    [Fact]
    public void Read_AnOrganizationForcingTheSwitchOn_AnswersOnHeldByTheOrganization()
    {
        // Act
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(organization: ForcingOn));

        // Assert
        Assert.Equal(new ForcedClientTelemetry(Enabled: true, SettingsPolicyScope.Organization), forced);
    }

    [Fact]
    public void Read_AnOrganizationForcingTheSwitchOff_AnswersOffHeldByTheOrganization()
    {
        // Act
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(organization: ForcingOff));

        // Assert
        Assert.Equal(new ForcedClientTelemetry(Enabled: false, SettingsPolicyScope.Organization), forced);
    }

    /// <summary>The order every forced value resolves in: the wider scope overrules the narrower one, so an organization holding the switch off is overruled by a deployment holding it on.</summary>
    [Fact]
    public void Read_TheDeploymentAndTheOrganizationForcingDifferentAnswers_AnswersTheDeployments()
    {
        // Act
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(deployment: ForcingOn, organization: ForcingOff));

        // Assert
        Assert.Equal(new ForcedClientTelemetry(Enabled: true, SettingsPolicyScope.Deployment), forced);
    }

    /// <summary>A person in no organization has nobody in the organization's place, so the deployment's policy is the whole of what governs them.</summary>
    [Fact]
    public void Read_APersonInNoOrganization_IsGovernedByTheDeploymentsPolicyAlone()
    {
        // Act
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(deployment: ForcingOff));

        // Assert
        Assert.Equal(new ForcedClientTelemetry(Enabled: false, SettingsPolicyScope.Deployment), forced);
    }

    /// <summary>A policy forcing other settings forces nothing about the switch, so the person's own answer stands.</summary>
    [Theory]
    [InlineData("{}", null)]
    [InlineData("{}", "{}")]
    [InlineData(ForcingSomethingElse, ForcingSomethingElse)]
    public void Read_PoliciesThatDoNotForceTheSwitch_AnswersNothing(string deployment, string? organization)
    {
        // Act
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(deployment, organization));

        // Assert
        Assert.Null(forced);
    }

    /// <summary>A policy this build does not judge as one forces nothing here; the record it governs is held back whole instead.</summary>
    [Fact]
    public void Read_APolicyThisBuildDoesNotReadAsOne_AnswersNothing()
    {
        // Act
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(
            deployment: ForcingOn,
            organization: """{"Users":{"Defaults":{"ClientPreferences":{"TelemetryEnabled":true}}}}"""));

        // Assert
        Assert.Null(forced);
    }

    [Theory]
    [InlineData(ForcingOn, null, "the deployment", "on")]
    [InlineData("{}", ForcingOff, "the organization", "off")]
    public void DescribeRefusal_AHeldSwitch_NamesTheSwitchWhatItIsForcedToAndWhoHoldsIt(
        string deployment,
        string? organization,
        string holder,
        string answer)
    {
        // Arrange
        var forced = ForcedClientTelemetry.Read(StatedPolicies.Governing(deployment, organization));

        // Act
        var refusal = forced!.DescribeRefusal();

        // Assert
        Assert.Contains("TelemetryEnabled", refusal, StringComparison.Ordinal);
        Assert.Contains($"held by {holder}", refusal, StringComparison.Ordinal);
        Assert.Contains($"forces it {answer}", refusal, StringComparison.Ordinal);
    }
}
