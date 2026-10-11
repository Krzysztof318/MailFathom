// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Policies;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

public sealed class GovernedRecordChangeTests
{
    private const string Nothing = "{}";
    private const string OrganizationForcesPolish = """{"Users":{"Forced":{"Language":"Polish"}}}""";
    private const string StandingEnglish = """{"Language":"English","TimeZone":"Europe/Paris"}""";

    private static readonly Guid Organization = Guid.Parse("7c1f6f0e-8f0b-4a53-9d0f-3c0c7a5a9e11");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Judge_AChangeToAForcedProperty_IsRefusedToThePersonAndToAnAdministratorAlikeNamingWhoHoldsIt(bool byThePerson)
    {
        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            Layers(Nothing, OrganizationForcesPolish),
            StandingEnglish,
            """{"Language":"German","TimeZone":"Europe/Paris"}""",
            byThePerson);

        // Assert
        var refusal = Assert.Single(judged.Refusals);

        Assert.StartsWith("Language is held by the organization", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_AForcedPropertyForcedByTheDeployment_NamesTheDeployment()
    {
        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            Layers(OrganizationForcesPolish, Nothing),
            StandingEnglish,
            """{"TimeZone":"Europe/Paris"}""",
            byThePerson: false);

        // Assert
        Assert.StartsWith("Language is held by the deployment", Assert.Single(judged.Refusals), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"Language":"English","TimeZone":"Europe/Paris"}""")]
    [InlineData("""{"Language":"polish","TimeZone":"Europe/Paris"}""")]
    public void Judge_AForcedPropertyCarriedThroughAsEitherReadShowedIt_PassesAndLeavesTheOwnValueAsItWas(string candidate)
    {
        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            Layers(Nothing, OrganizationForcesPolish),
            StandingEnglish,
            candidate,
            byThePerson: true);

        // Assert
        Assert.Empty(judged.Refusals);
        Assert.Equal(
            "English",
            SettingsDocumentTree.Find(GovernedRecordResolution.ReadDocument(judged.Json)!, "Language")?.GetValue<string>());
    }

    [Fact]
    public void Judge_AChangeBesideAForcedProperty_PassesAsItWasWritten()
    {
        // Arrange
        const string Candidate = """{"Language":"English","TimeZone":"Europe/Warsaw"}""";

        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            Layers(Nothing, OrganizationForcesPolish),
            StandingEnglish,
            Candidate,
            byThePerson: true);

        // Assert
        Assert.Empty(judged.Refusals);
        Assert.Same(Candidate, judged.Json);
    }

    [Fact]
    public void Judge_APropertyTheEditingRestrictionWithholds_IsRefusedToThePersonAndLeftToAnAdministrator()
    {
        // Arrange
        var layers = Layers("""{"Users":{"Editing":{"Mode":"NoneExcept","Properties":["Portrait"]}}}""");
        const string Candidate = """{"Language":"English","TimeZone":"Europe/Warsaw"}""";

        // Act
        var person = GovernedRecordChange.Judge(SettingsPolicySection.Users, layers, StandingEnglish, Candidate, byThePerson: true);
        var administrator = GovernedRecordChange.Judge(SettingsPolicySection.Users, layers, StandingEnglish, Candidate, byThePerson: false);

        // Assert
        Assert.StartsWith("TimeZone is held by the deployment", Assert.Single(person.Refusals), StringComparison.Ordinal);
        Assert.Empty(administrator.Refusals);
    }

    [Fact]
    public void Judge_APropertyOnlyAnAdministratorWrites_IsRefusedToThePersonWhateverAnyPolicySays()
    {
        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            SettingsPolicyLayers.Nothing,
            StandingEnglish,
            """{"Language":"English","TimeZone":"Europe/Paris","ClientTelemetryLevel":"Debug","EndpointAccess":{"McpEndpoint":false}}""",
            byThePerson: true);

        // Assert
        Assert.Equal(2, judged.Refusals.Count);
        Assert.Contains(judged.Refusals, refusal => refusal.StartsWith("ClientTelemetryLevel is decided by whoever administers", StringComparison.Ordinal));
        Assert.Contains(judged.Refusals, refusal => refusal.StartsWith("EndpointAccess:McpEndpoint is decided by whoever administers", StringComparison.Ordinal));
    }

    [Fact]
    public void Judge_AnAdministratorOnlyPropertyStatedAsTheBuildAlreadyTakesIt_IsCarriedThroughRatherThanWritten()
    {
        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            SettingsPolicyLayers.Nothing,
            StandingEnglish,
            """{"Language":"English","TimeZone":"Europe/Paris","EndpointAccess":{"McpEndpoint":true,"ClientEndpoint":"true"}}""",
            byThePerson: true);

        // Assert
        Assert.Empty(judged.Refusals);
        Assert.Null(SettingsDocumentTree.Find(GovernedRecordResolution.ReadDocument(judged.Json)!, "EndpointAccess:McpEndpoint"));
    }

    [Fact]
    public void Judge_ACandidateThatIsNoDocument_IsLeftForTheBinderToRefuse()
    {
        // Act
        var judged = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            Layers(Nothing, OrganizationForcesPolish),
            StandingEnglish,
            "not json",
            byThePerson: true);

        // Assert
        Assert.Empty(judged.Refusals);
        Assert.Equal("not json", judged.Json);
    }

    private static SettingsPolicyLayers Layers(string deployment, string? organization = null) =>
        SettingsPolicyLayers.Read(
            new GoverningSettingsPolicies(
                new SettingsPolicyDocument(OrganizationId: null, deployment, Version: 1),
                organization is null ? null : new SettingsPolicyDocument(Organization, organization, Version: 1)),
            SettingsPolicySection.Users,
            out _)
        ?? throw new InvalidOperationException("The policy a test stated is not one this build judges as a settings policy.");
}
