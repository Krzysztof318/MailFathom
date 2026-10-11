// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers a user's record as the settings policies governing them decide it: what a write is refused, what it may
/// carry through, which value reaches the user's row, and what a read reports per property.
/// </summary>
public sealed class UserRecordAdministrationPolicyTests
{
    private const string ForcingWarsaw = """{"Users":{"Forced":{"TimeZone":"Europe/Warsaw"}}}""";
    private const string WithholdingTheZoneAndThePortrait =
        """{"Users":{"Editing":{"Mode":"AllExcept","Properties":["TimeZone","Portrait"]}}}""";

    private const string DefaultingPolish = """{"Users":{"Defaults":{"Language":"Polish"}}}""";
    private const string ForcingOffMcp = """{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""";
    private const string DefaultingOffMcp = """{"Users":{"Defaults":{"EndpointAccess":{"McpEndpoint":false}}}}""";
    private const string NotAPolicy = """{"Users":{"Forced":{"Dialect":"Silesian"}}}""";
    private const string EnglishInParis = """{"Language":"English","TimeZone":"Europe/Paris"}""";

    private static readonly UserId Alex = SyntheticUser.Deployment;

    /// <summary>A forced value binds everybody, so an administrator stating another is refused naming the setting and who holds it.</summary>
    [Fact]
    public async Task ApplyRecordAsync_AnAdministratorMovingASettingAPolicyForces_IsRefusedNamingWhoHoldsIt()
    {
        // Arrange
        var deployment = Administered(EnglishInParis, StatedPolicies.Governing(organization: ForcingWarsaw));

        // Act
        var outcome = await deployment.Records.ApplyRecordAsync(
            Alex,
            """{"Language":"English","TimeZone":"Asia/Tokyo"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.True(outcome.IsHeld);
        Assert.StartsWith("TimeZone is held by the organization", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>An editing restriction binds the person the record is about and nobody administering them.</summary>
    [Fact]
    public async Task ApplyRecordAsync_AnAdministratorChangingASettingWithheldFromThePerson_Commits()
    {
        // Arrange
        var deployment = Administered(
            EnglishInParis,
            StatedPolicies.Governing(deployment: WithholdingTheZoneAndThePortrait));

        // Act
        var outcome = await deployment.Records.ApplyRecordAsync(
            Alex,
            """{"Language":"English","TimeZone":"Asia/Tokyo"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    [Fact]
    public async Task ApplyOwnRecordAsync_APersonChangingASettingWithheldFromThem_IsRefusedNamingWhoHoldsIt()
    {
        // Arrange
        var deployment = SignedIn(
            EnglishInParis,
            StatedPolicies.Governing(deployment: WithholdingTheZoneAndThePortrait));

        // Act
        var outcome = await deployment.Records.ApplyOwnRecordAsync(
            """{"Language":"English","TimeZone":"Asia/Tokyo"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsHeld);
        Assert.StartsWith("TimeZone is held by the deployment", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// A caller that read the value in force and sends it back is changing nothing, so the save passes — and the
    /// record keeps the value somebody stated on it, which is what comes back when the force is lifted.
    /// </summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_AForcedSettingCarriedThroughAsItIsServed_CommitsTheRestAndKeepsTheRecordsOwnValue()
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(organization: ForcingWarsaw));

        // Act
        var outcome = await deployment.Records.ApplyOwnRecordAsync(
            """{"Language":"Polish","TimeZone":"Europe/Warsaw"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await deployment.Store.Received(1).CommitAsync(
            Alex,
            Arg.Is<string>(json =>
                json!.Contains("Europe/Paris", StringComparison.Ordinal)
                && json.Contains("Polish", StringComparison.Ordinal)
                && !json.Contains("Europe/Warsaw", StringComparison.Ordinal)),
            Arg.Any<UserEndpointAccess>(),
            3,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A save whose only difference is a held setting stated as it is served spends no version.</summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_OnlyAForcedSettingStatedAsItIsServed_WritesNothing()
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(organization: ForcingWarsaw));

        // Act
        var outcome = await deployment.Records.ApplyOwnRecordAsync(
            """{"Language":"English","TimeZone":"Europe/Warsaw"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsSettled);
        Assert.False(outcome.IsCommitted);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>The language is required of a record only where no policy supplies it.</summary>
    [Theory]
    [InlineData(DefaultingPolish, true)]
    [InlineData("{}", false)]
    public async Task ApplyRecordAsync_ARecordStatingNoLanguage_IsCommittedOnlyWhereAPolicySuppliesOne(
        string deploymentPolicy,
        bool committed)
    {
        // Arrange
        var deployment = Administered("{}", StatedPolicies.Governing(deployment: deploymentPolicy));

        // Act
        var outcome = await deployment.Records.ApplyRecordAsync(
            Alex,
            """{"TimeZone":"Europe/Warsaw"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(committed, outcome!.IsCommitted);
    }

    /// <summary>
    /// The judgement that decides is the one made inside the commit, under the policies in force at that moment: a
    /// force that landed after the record was read still holds the write.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_APolicyThatMovedBetweenTheReadAndTheCommit_IsHeldByTheOneInForceAtTheCommit()
    {
        // Arrange
        var deployment = Administered(EnglishInParis, StatedPolicies.Governing(organization: ForcingWarsaw));
        deployment.ReadingUnder(Alex, StatedPolicies.Governing(organization: "{}"));

        // Act
        var outcome = await deployment.Records.ApplyRecordAsync(
            Alex,
            """{"Language":"English","TimeZone":"Asia/Tokyo"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsHeld);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// What a request authenticates against is the row, so a record's commit carries the switches the policies
    /// resolve onto it while the document keeps only what somebody stated.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_ARecordSavedUnderAPolicyForcingASwitch_CarriesTheForcedSwitchOntoTheRow()
    {
        // Arrange
        var deployment = Administered("""{"Language":"English"}""", StatedPolicies.Governing(deployment: ForcingOffMcp));

        // Act
        var outcome = await deployment.Records.ApplyRecordAsync(
            Alex,
            """{"Language":"Polish"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await deployment.Store.Received(1).CommitAsync(
            Alex,
            Arg.Is<string>(json => !json!.Contains("McpEndpoint", StringComparison.Ordinal)),
            new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            3,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyRecordAsync_APolicyThisBuildDoesNotRead_IsRefusedRatherThanJudgedOnPartOfWhatGovernsTheRecord()
    {
        // Arrange
        var deployment = Administered(EnglishInParis, StatedPolicies.Governing(deployment: NotAPolicy));

        // Act
        var outcome = await deployment.Records.ApplyRecordAsync(
            Alex,
            """{"Language":"Polish","TimeZone":"Europe/Paris"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.StartsWith("The settings policy of the deployment", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// A switch a policy forces is not an administrator's to move either, whatever the record states beneath the
    /// force: one already stating the position asked for would otherwise carry it through and answer as settled.
    /// </summary>
    [Theory]
    [InlineData("""{"Language":"English","EndpointAccess":{"McpEndpoint":"false"}}""")]
    [InlineData("""{"Language":"English","EndpointAccess":{"McpEndpoint":"true"}}""")]
    [InlineData("""{"Language":"English"}""")]
    public async Task SetEndpointAccessAsync_ASwitchAPolicyForcesAskedTheOtherWay_IsRefusedNamingWhoHoldsIt(string record)
    {
        // Arrange
        var deployment = Administered(record, StatedPolicies.Governing(deployment: ForcingOffMcp));

        // Act
        var written = await deployment.Records.SetEndpointAccessAsync(
            Alex,
            mcpEndpoint: true,
            clientEndpoint: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written!.Outcome.IsHeld);
        Assert.StartsWith(
            "EndpointAccess:McpEndpoint is held by the deployment",
            Assert.Single(written.Outcome.Messages),
            StringComparison.Ordinal);
        Assert.Equal(new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), written.EndpointAccess);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>The switches a write is measured against are the ones the user is served on, so asking for where a policy already puts them writes nothing.</summary>
    [Fact]
    public async Task SetEndpointAccessAsync_TheSwitchesTheUserIsAlreadyServedOnUnderAPolicy_WritesNothingAndAnswersThem()
    {
        // Arrange
        var deployment = Administered("""{"Language":"English"}""", StatedPolicies.Governing(deployment: ForcingOffMcp));

        // Act
        var written = await deployment.Records.SetEndpointAccessAsync(
            Alex,
            mcpEndpoint: false,
            clientEndpoint: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written!.Outcome.IsSettled);
        Assert.Equal(new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), written.EndpointAccess);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// A default is what an administrator overrides for one person: a switch a policy's default turns off, which the
    /// record never stated, is written on rather than read as already on.
    /// </summary>
    [Fact]
    public async Task SetEndpointAccessAsync_ASwitchADefaultInAPolicyTurnsOffAskedOn_IsWrittenIntoTheRecord()
    {
        // Arrange
        var deployment = Administered("""{"Language":"English"}""", StatedPolicies.Governing(organization: DefaultingOffMcp));

        // Act
        var written = await deployment.Records.SetEndpointAccessAsync(
            Alex,
            mcpEndpoint: true,
            clientEndpoint: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written!.Outcome.IsCommitted);
        Assert.Equal(UserEndpointAccess.Everywhere, written.EndpointAccess);
        await deployment.Store.Received(1).CommitAsync(
            Alex,
            Arg.Is<string>(json =>
                json!.Contains("\"McpEndpoint\":\"true\"", StringComparison.Ordinal)
                && !json.Contains("ClientEndpoint", StringComparison.Ordinal)),
            UserEndpointAccess.Everywhere,
            3,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ForcingWarsaw)]
    [InlineData(WithholdingTheZoneAndThePortrait)]
    public async Task ChangeOwnTimeZoneAsync_AZoneAPolicyHolds_IsRefusedNamingWhoHoldsIt(string organizationPolicy)
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(organization: organizationPolicy));

        // Act
        var change = await deployment.Records.ChangeOwnTimeZoneAsync(Zone("Asia/Tokyo"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(change.UserHeld);
        Assert.StartsWith("TimeZone is held by the organization", change.RefusalMessage, StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>Choosing the zone already in force changes nothing, so it is answered as settled rather than refused.</summary>
    [Fact]
    public async Task ChangeOwnTimeZoneAsync_TheZoneAPolicyForcesChosen_IsSettledWithoutCommitting()
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(organization: ForcingWarsaw));

        // Act
        var change = await deployment.Records.ChangeOwnTimeZoneAsync(Zone("Europe/Warsaw"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnSettingChange.SettledServing("Europe/Warsaw"), change);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// A person may state their record's own zone under a force, which writes nothing, and is answered the zone the
    /// policy forces rather than the one they asked for: no reader of theirs uses the record's.
    /// </summary>
    [Fact]
    public async Task ChangeOwnTimeZoneAsync_TheRecordsOwnZoneChosenUnderAForce_IsSettledServingTheForcedZone()
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(organization: ForcingWarsaw));

        // Act
        var change = await deployment.Records.ChangeOwnTimeZoneAsync(Zone("Europe/Paris"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnSettingChange.SettledServing("Europe/Warsaw"), change);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// A policy this build does not read holds the record, so a change to one setting is answered with the sentence
    /// naming the policy rather than failing — whether the zone is a new one or the one the record already states.
    /// </summary>
    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("Europe/Paris")]
    public async Task ChangeOwnTimeZoneAsync_APolicyThisBuildDoesNotRead_IsRefusedNamingThePolicy(string zoneId)
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(organization: NotAPolicy));

        // Act
        var change = await deployment.Records.ChangeOwnTimeZoneAsync(Zone(zoneId), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(change.UserHeld);
        Assert.Null(change.Served);
        Assert.StartsWith("The settings policy of this record's organization", change.RefusalMessage, StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// The same policy holds the portrait link, which is answered as a refusal so the route that wrote the file for
    /// it removes the file again rather than leaving it stored and unlinked.
    /// </summary>
    [Fact]
    public async Task RelinkOwnPortraitAsync_APolicyThisBuildDoesNotRead_IsRefusedNamingThePolicyAndLinksNothing()
    {
        // Arrange
        var deployment = SignedIn(EnglishInParis, StatedPolicies.Governing(deployment: NotAPolicy));

        // Act
        var relinking = await deployment.Records.RelinkOwnPortraitAsync(
            StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-0000000000b1")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(relinking.UserHeld);
        Assert.StartsWith("The settings policy of the deployment", relinking.Refusal, StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    [Fact]
    public async Task RelinkOwnPortraitAsync_APortraitAPolicyWithholdsFromThePerson_IsRefusedAndLinksNothing()
    {
        // Arrange
        var deployment = SignedIn(
            EnglishInParis,
            StatedPolicies.Governing(deployment: WithholdingTheZoneAndThePortrait));

        // Act
        var relinking = await deployment.Records.RelinkOwnPortraitAsync(
            StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-0000000000b1")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(relinking.UserHeld);
        Assert.StartsWith("Portrait is held by the deployment", relinking.Refusal, StringComparison.Ordinal);
        await AssertNothingCommittedAsync(deployment);
    }

    /// <summary>
    /// A read answers the record's own document, because that is what a save is composed over, and beside it each
    /// property as the policies resolve it: the value in force, the step that supplied it, and who holds it.
    /// </summary>
    [Fact]
    public async Task ReadRecordAsync_ARecordUnderPolicies_ReportsEachPropertyAsResolvedBesideTheRecordsOwnDocument()
    {
        // Arrange
        var deployment = Administered(
            """{"TimeZone":"Europe/Paris"}""",
            StatedPolicies.Governing(deployment: DefaultingPolish, organization: ForcingWarsaw));

        // Act
        var reading = await deployment.Records.ReadRecordAsync(Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Europe/Paris", reading!.Json, StringComparison.Ordinal);
        Assert.Null(reading.Unresolved);

        var zone = reading.Properties.Single(property => property.Property.Path == "TimeZone");
        Assert.Equal(SettingsValueSource.OrganizationForced, zone.Source);
        Assert.Equal("Europe/Warsaw", zone.EffectiveValue!.GetValue<string>());
        Assert.Equal(SettingsPolicyScope.Organization, zone.HeldBy);
        Assert.False(zone.IsAnAdministratorsToChange);

        var language = reading.Properties.Single(property => property.Property.Path == "Language");
        Assert.Equal(SettingsValueSource.DeploymentDefault, language.Source);
        Assert.Equal("Polish", language.EffectiveValue!.GetValue<string>());
        Assert.True(language.IsThePersonsToChange);
    }

    [Fact]
    public async Task ReadRecordAsync_APolicyThisBuildDoesNotRead_ReportsNoPropertyAndSaysWhy()
    {
        // Arrange
        var deployment = Administered(EnglishInParis, StatedPolicies.Governing(organization: NotAPolicy));

        // Act
        var reading = await deployment.Records.ReadRecordAsync(Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(reading!.Properties);
        Assert.StartsWith("The settings policy of this record's organization", reading.Unresolved, StringComparison.Ordinal);
    }

    private static UserRecordDeployment Administered(string record, GoverningSettingsPolicies policies) =>
        Holding(
            new UserRecordDeployment([MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite]),
            record,
            policies);

    private static UserRecordDeployment SignedIn(string record, GoverningSettingsPolicies policies) =>
        Holding(
            new UserRecordDeployment([MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite], actingFor: Alex),
            record,
            policies);

    private static UserRecordDeployment Holding(UserRecordDeployment deployment, string record, GoverningSettingsPolicies policies)
    {
        deployment.Holding(Alex, record, version: 3);
        deployment.GovernedBy(Alex, policies);

        return deployment;
    }

    private static Task<long?> AssertNothingCommittedAsync(UserRecordDeployment deployment) =>
        deployment.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);

    private static UserTimeZone Zone(string zoneId) =>
        UserTimeZone.TryRead(zoneId, out var zone)
            ? zone
            : throw new InvalidOperationException($"This host knows no time zone called '{zoneId}'.");
}
