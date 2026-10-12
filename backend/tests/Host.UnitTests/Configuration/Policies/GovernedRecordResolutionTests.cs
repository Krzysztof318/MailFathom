// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Policies;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

public sealed class GovernedRecordResolutionTests
{
    private const string Nothing = "{}";

    private static readonly Guid Organization = Guid.Parse("7c1f6f0e-8f0b-4a53-9d0f-3c0c7a5a9e11");

    [Theory]
    [InlineData(
        """{"Users":{"Forced":{"Language":"Polish"},"Defaults":{"Language":"English"}}}""",
        """{"Users":{"Forced":{"Language":"English"}}}""",
        """{"Language":"English"}""",
        "DeploymentForced")]
    [InlineData(
        """{"Users":{"Defaults":{"Language":"English"}}}""",
        """{"Users":{"Forced":{"Language":"Polish"}}}""",
        """{"Language":"English"}""",
        "OrganizationForced")]
    [InlineData(
        """{"Users":{"Defaults":{"Language":"English"}}}""",
        """{"Users":{"Defaults":{"Language":"English"}}}""",
        """{"Language":"Polish"}""",
        "Record")]
    [InlineData(
        """{"Users":{"Defaults":{"Language":"English"}}}""",
        """{"Users":{"Defaults":{"Language":"Polish"}}}""",
        Nothing,
        "OrganizationDefault")]
    [InlineData(
        """{"Users":{"Defaults":{"Language":"Polish"}}}""",
        Nothing,
        Nothing,
        "DeploymentDefault")]
    public void Resolve_EachStepOfTheOrder_SuppliesTheValueWhereNoEarlierStepStatesOne(
        string deployment,
        string organization,
        string own,
        string expected)
    {
        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, Layers(deployment, organization), own);

        // Assert
        var language = resolved.Find("Language");

        Assert.Equal(expected, language.Source.Name);
        Assert.Equal("Polish", language.EffectiveValue?.GetValue<string>());
        Assert.Equal("Polish", Bind(resolved.EffectiveJson).Language);
    }

    [Fact]
    public void Resolve_APropertyNothingStates_IsWhatTheBuildTakesForIt()
    {
        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, SettingsPolicyLayers.Nothing, Nothing);

        // Assert
        Assert.All(resolved.Properties, property => Assert.Equal(
            property.Property.Accessors.Count == 0 ? SettingsValueSource.Record : SettingsValueSource.BuiltIn,
            property.Source));
        Assert.Null(resolved.Find("Language").EffectiveValue);
        Assert.True(resolved.Find("EndpointAccess:McpEndpoint").EffectiveValue?.GetValue<bool>());
        Assert.Equal(Nothing, resolved.EffectiveJson);
    }

    [Fact]
    public void Resolve_AUserInNoOrganization_ReadsTheDeploymentsPolicyAlone()
    {
        // Arrange
        var layers = Layers("""{"Users":{"Forced":{"TimeZone":"Europe/Warsaw"},"Defaults":{"Language":"Polish"}}}""");

        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, """{"TimeZone":"Europe/Paris"}""");

        // Assert
        Assert.Null(layers.Organization);
        Assert.Equal(SettingsValueSource.DeploymentForced, resolved.Find("TimeZone").Source);
        Assert.Equal(SettingsValueSource.DeploymentDefault, resolved.Find("Language").Source);
        Assert.Equal("Europe/Warsaw", Bind(resolved.EffectiveJson).TimeZone);
    }

    [Fact]
    public void Resolve_AnOrganizationThatStatesNothing_LeavesTheRecordAndTheDeploymentToAnswer()
    {
        // Arrange
        var layers = Layers("""{"Users":{"Defaults":{"Language":"Polish"}}}""", Nothing);

        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, """{"TimeZone":"Europe/Paris"}""");

        // Assert
        Assert.Equal(SettingsValueSource.DeploymentDefault, resolved.Find("Language").Source);
        Assert.Equal(SettingsValueSource.Record, resolved.Find("TimeZone").Source);
        Assert.All(resolved.Properties, property => Assert.Null(property.HeldBy));
    }

    [Fact]
    public void Resolve_AForcedValue_IsNeverWrittenIntoTheRecordsOwnDocument()
    {
        // Arrange
        const string Own = """{"language":"English","TimeZone":"Europe/Paris"}""";

        // Act
        var forced = GovernedRecordResolution.Resolve(
            SettingsPolicySection.Users,
            Layers("""{"Users":{"Forced":{"Language":"Polish"}}}"""),
            Own);
        var lifted = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, SettingsPolicyLayers.Nothing, Own);

        // Assert
        Assert.Equal("Polish", Bind(forced.EffectiveJson).Language);
        Assert.Equal("English", Bind(lifted.EffectiveJson).Language);
        Assert.Equal(SettingsValueSource.Record, lifted.Find("Language").Source);
    }

    [Fact]
    public void Resolve_OnePropertyOfABlockForced_LeavesItsSiblingToResolveOnItsOwn()
    {
        // Arrange
        var layers = Layers("""{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""");

        // Act
        var resolved = GovernedRecordResolution.Resolve(
            SettingsPolicySection.Users,
            layers,
            """{"EndpointAccess":{"McpEndpoint":"true","ClientEndpoint":"false"}}""");

        // Assert
        var access = Bind(resolved.EffectiveJson).EndpointAccess;

        Assert.False(access.McpEndpoint);
        Assert.False(access.ClientEndpoint);
        Assert.Equal(SettingsValueSource.DeploymentForced, resolved.Find("EndpointAccess:McpEndpoint").Source);
        Assert.Equal(SettingsValueSource.Record, resolved.Find("EndpointAccess:ClientEndpoint").Source);
    }

    [Theory]
    [InlineData(
        """{"Users":{"Editing":{"Mode":"AllExcept","Properties":["Language"]}}}""",
        """{"Users":{"Editing":{"Mode":"AllExcept","Properties":["Language"]}}}""",
        "Deployment")]
    [InlineData(
        Nothing,
        """{"Users":{"Editing":{"Mode":"AllExcept","Properties":["Language"]}}}""",
        "Organization")]
    [InlineData(
        """{"Users":{"Editing":{"Mode":"NoneExcept","Properties":["TimeZone"]}}}""",
        """{"Users":{"Editing":{"Mode":"NoneExcept","Properties":["Language"]}}}""",
        "Deployment")]
    [InlineData(
        """{"Users":{"Editing":{"Mode":"NoneExcept","Properties":["Language"]}}}""",
        """{"Users":{"Editing":{"Mode":"NoneExcept","Properties":["TimeZone"]}}}""",
        "Organization")]
    public void Resolve_TwoEditingRestrictions_TheMoreRestrictiveAnswerWins(
        string deployment,
        string organization,
        string expected)
    {
        // Act
        var language = GovernedRecordResolution
            .Resolve(SettingsPolicySection.Users, Layers(deployment, organization), Nothing)
            .Find("Language");

        // Assert
        Assert.Equal(expected, language.WithheldBy?.Name);
        Assert.False(language.IsThePersonsToChange);
        Assert.True(language.IsAnAdministratorsToChange);
    }

    [Fact]
    public void Resolve_AnEditingListNamingABlock_CoversEveryPropertyBeneathIt()
    {
        // Arrange
        var layers = Layers("""{"Users":{"Editing":{"Mode":"AllExcept","Properties":["EndpointAccess","DisplayName"]}}}""");

        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, Nothing);

        // Assert
        Assert.Equal(SettingsPolicyScope.Deployment, resolved.Find("EndpointAccess:McpEndpoint").WithheldBy);
        Assert.Equal(SettingsPolicyScope.Deployment, resolved.Find("EndpointAccess:ClientEndpoint").WithheldBy);
        Assert.Equal(SettingsPolicyScope.Deployment, resolved.Find("DisplayName").WithheldBy);
        Assert.Null(resolved.Find("Language").WithheldBy);
    }

    [Fact]
    public void Resolve_APropertyOnlyAnAdministratorWrites_IsNeverThePersonsToChange()
    {
        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, SettingsPolicyLayers.Nothing, Nothing);

        // Assert
        Assert.False(resolved.Find("ClientTelemetryLevel").IsThePersonsToChange);
        Assert.False(resolved.Find("EndpointAccess:ClientEndpoint").IsThePersonsToChange);
        Assert.True(resolved.Find("Language").IsThePersonsToChange);
        Assert.True(resolved.Find("Portrait").IsThePersonsToChange);
    }

    [Fact]
    public void Resolve_ADocumentThatIsNotAnObject_IsHandedBackForTheBinderToRefuse()
    {
        // Act
        var resolved = GovernedRecordResolution.Resolve(
            SettingsPolicySection.Users,
            Layers("""{"Users":{"Forced":{"Language":"Polish"}}}"""),
            "[1, 2");

        // Assert
        Assert.Equal("[1, 2", resolved.EffectiveJson);
    }

    [Fact]
    public void Read_APolicyThisBuildDoesNotJudgeAsOne_AnswersNoLayersAndNamesTheScope()
    {
        // Arrange
        var policies = new GoverningSettingsPolicies(
            new SettingsPolicyDocument(OrganizationId: null, Nothing, Version: 3),
            new SettingsPolicyDocument(Organization, """{"Users":{"Forced":{"Colour":"Teal"}}}""", Version: 4));

        // Act
        var layers = SettingsPolicyLayers.Read(policies, SettingsPolicySection.Users, out var unreadable);

        // Assert
        Assert.Null(layers);
        Assert.Equal(SettingsPolicyScope.Organization, unreadable);
    }

    [Fact]
    public void Read_AForcedClientPreference_IsNoPropertyOfTheRecordItSitsBeside()
    {
        // Arrange
        var layers = Layers("""{"Users":{"Forced":{"ClientPreferences":{"TelemetryEnabled":true},"Language":"Polish"}}}""");

        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, Nothing);

        // Assert
        Assert.Equal("Polish", Bind(resolved.EffectiveJson).Language);
    }

    /// <summary>
    /// What keeps a property added to a user's record from needing a change to the resolution: every property the
    /// record's type declares that a policy may state a value for is forced here, by name and from nothing but the
    /// type, and the record bound from the answer has to carry the forced value. A new property of a type this cannot
    /// sample fails rather than passing unexamined.
    /// </summary>
    [Fact]
    public void Resolve_EveryPropertyAUsersRecordDeclares_IsGovernedWithoutTheResolutionNamingIt()
    {
        // Arrange
        var governed = SettingsPolicySection.Users.Properties.All
            .Where(property => property.Shape != GovernablePropertyShape.Block
                && property.Accessors.Count > 0
                && property.PropertyClass != SettingsPolicyPropertyClass.Identity)
            .ToArray();
        var builtIn = new UserAccountOptions();
        var forced = new JsonObject();

        foreach (var property in governed)
        {
            SettingsDocumentTree.Replace(forced, property.Path, SampleFor(property, builtIn));
        }

        var layers = Layers(new JsonObject { ["Users"] = new JsonObject { ["Forced"] = forced } }.ToJsonString());

        // Act
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, Nothing);

        // Assert
        var bound = Bind(resolved.EffectiveJson);

        Assert.NotEmpty(governed);
        Assert.All(governed, property =>
        {
            Assert.Equal(SettingsValueSource.DeploymentForced, resolved.Find(property.Path).Source);
            Assert.True(
                SettingsDocumentTree.StateTheSame(SampleFor(property, builtIn), JsonValue.Create(property.ReadFrom(bound).Value?.ToString())),
                $"{property.Path} was forced and the record bound from the resolution does not carry the forced value.");
        });
    }

    private static JsonValue SampleFor(GovernableProperty property, UserAccountOptions builtIn) => property.Path switch
    {
        "Language" => JsonValue.Create("Polish"),
        "TimeZone" => JsonValue.Create("Europe/Warsaw"),
        "ClientTelemetryLevel" => JsonValue.Create("Debug"),
        _ when property.ValueType == typeof(bool) => JsonValue.Create(!(bool)property.ReadFrom(builtIn).Value!),
        _ => throw new InvalidOperationException(
            $"{property.Path} is a property of a user's record this test has no sample value for. Add one, so that the resolution is shown to govern it."),
    };

    private static SettingsPolicyLayers Layers(string deployment, string? organization = null)
    {
        var layers = SettingsPolicyLayers.Read(
            new GoverningSettingsPolicies(
                new SettingsPolicyDocument(OrganizationId: null, deployment, Version: 1),
                organization is null ? null : new SettingsPolicyDocument(Organization, organization, Version: 1)),
            SettingsPolicySection.Users,
            out _);

        return layers ?? throw new InvalidOperationException("The policy a test stated is not one this build judges as a settings policy.");
    }

    private static UserAccountOptions Bind(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build()
            .Get<UserAccountOptions>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new UserAccountOptions();
    }
}
