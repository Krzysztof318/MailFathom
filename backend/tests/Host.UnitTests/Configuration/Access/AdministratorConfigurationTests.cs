// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers what the administrative section reads across every administrator at once.</summary>
/// <remarks>
/// Each administrator is valid or not on its own; what this reads is what only the whole list can answer — whether two
/// administrators share a name, whether a token could be bound to two of them, whether one authorization server is
/// described two ways — and which administrator each presented credential resolves to.
/// </remarks>
public sealed class AdministratorConfigurationTests
{
    private const string SectionName = "AdminEndpoint";

    private const string Resource = "https://mail.example.test/api/admin";

    private const string Issuer = "https://sso.example.test/realms/mailfathom";

    [Fact]
    public void FindConfigurationErrors_TwoAdministratorsWithTheirOwnNames_ReportsNothing()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.AdministratorWithApiKey("alice"),
            ConfiguredAuthentication.AdministratorWithApiKey("ci-pipeline"),
        ];

        // Act, Assert
        Assert.Empty(AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators));
    }

    /// <summary>Two records reading one name would be one person to anybody auditing them, whatever the case they were typed in.</summary>
    [Theory]
    [InlineData("alice")]
    [InlineData("Alice")]
    public void FindConfigurationErrors_ASecondAdministratorRepeatingAName_IsRefusedAtTheLaterOne(string repeated)
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.AdministratorWithApiKey("alice"),
            ConfiguredAuthentication.Administrator(repeated, ConfiguredAuthentication.ApiKey("alice-laptop")),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith($"{SectionName}:Administrators:1:Name — '{repeated}' repeats a name", reported, StringComparison.Ordinal);
    }

    /// <summary>A subject bound twice would leave the name an act is attributed to decided by configuration order.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAdministratorsNamingOneSubjectAtOneServer_IsRefusedAtTheLaterSubject()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
            ConfiguredAuthentication.Administrator("bob", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith(
            $"{SectionName}:Administrators:1:Credentials:0:OAuth:AuthorizationServers:0:AuthorizedSubjects:0 — 'alice-subject' is already an administrator's subject",
            reported,
            StringComparison.Ordinal);
    }

    /// <summary>One server shared by several administrators is the ordinary arrangement, and it registers one validator.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAdministratorsSigningInThroughOneServer_ReportsNothing()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
            ConfiguredAuthentication.Administrator("bob", ConfiguredAuthentication.OAuthFor(Resource, subject: "bob-subject")),
        ];

        // Act, Assert
        Assert.Empty(AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators));
        Assert.Single(AdministratorConfiguration.DistinctAuthorizationServersIn(administrators));
    }

    [Fact]
    public void FindConfigurationErrors_OneServerNameCarryingTwoIssuers_IsRefused()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
            ConfiguredAuthentication.Administrator(
                "bob",
                ConfiguredAuthentication.OAuthFor(Resource, issuer: "https://sso.partner.test", subject: "bob-subject")),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith(
            $"{SectionName}:Administrators:1:Credentials:0:OAuth:AuthorizationServers:0:Name — 'workforce' repeats a name",
            reported,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_OneIssuerCarriedByTwoServerNames_IsRefused()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
            ConfiguredAuthentication.Administrator(
                "bob",
                ConfiguredAuthentication.OAuthFor(Resource, authorizationServerName: "staff", subject: "bob-subject")),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith(
            $"{SectionName}:Administrators:1:Credentials:0:OAuth:AuthorizationServers:0:Issuer — this issuer repeats one",
            reported,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_OneServerWrittenWithTwoDiscoveryDocuments_IsRefused()
    {
        // Arrange
        var bobCredential = ConfiguredAuthentication.OAuthFor(Resource, subject: "bob-subject");
        bobCredential.OAuth!.AuthorizationServers[0].MetadataAddress =
            "https://sso.example.test/realms/mailfathom/.well-known/openid-configuration";

        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
            ConfiguredAuthentication.Administrator("bob", bobCredential),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith(
            $"{SectionName}:Administrators:1:Credentials:0:OAuth:AuthorizationServers:0:MetadataAddress — 'workforce' is written elsewhere",
            reported,
            StringComparison.Ordinal);
    }

    /// <summary>The endpoint publishes one protected resource, so a second resource keeps the refusal it always had.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAdministratorsNamingDifferentResources_IsRefusedAtTheLaterResource()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject")),
            ConfiguredAuthentication.Administrator(
                "bob",
                ConfiguredAuthentication.OAuthFor("https://other.example.test/api/admin", subject: "bob-subject")),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith(
            $"{SectionName}:Administrators:1:Credentials:0:OAuth:Resource — every OAuth entry names the same resource",
            reported,
            StringComparison.Ordinal);
    }

    /// <summary>Cross-administrator rules read validated values, so an administrator faulty on its own is reported alone rather than raising.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAdministratorFaultyOnItsOwn_ReportsOnlyItsOwnFaults()
    {
        // Arrange
        AdministratorOptions[] administrators =
        [
            ConfiguredAuthentication.AdministratorWithApiKey("alice"),
            ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor("not a uri")),
        ];

        // Act
        var errors = AdministratorConfiguration.FindConfigurationErrors(SectionName, administrators);

        // Assert
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.StartsWith($"{SectionName}:Administrators:1:Credentials:0:OAuth", error, StringComparison.Ordinal));
    }

    /// <summary>
    /// The pair an absent grant and an emptied one make: identical to the binder, opposite in meaning. Keys written with
    /// a gap, as an environment-variable configuration writes them, name the refusal by the key the operator wrote.
    /// </summary>
    [Fact]
    public void ReadWhatTheBinderCannotSay_AnAbsentGrantAndAnEmptyOneUnderGappedKeys_TellsThemApartAndRecordsTheKeys()
    {
        // Arrange
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AdminEndpoint:Administrators:0:Name"] = "alice",
                ["AdminEndpoint:Administrators:0:Credentials:0:ApiKey:Name"] = "alice",
                ["AdminEndpoint:Administrators:2:Name"] = "auditor",
                ["AdminEndpoint:Administrators:2:Credentials:3:ApiKey:Name"] = "auditor",
                ["AdminEndpoint:Administrators:2:Permissions"] = string.Empty,
            })
            .Build()
            .GetSection(SectionName);

        var alice = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        var auditor = ConfiguredAuthentication.AdministratorWithApiKey("auditor");

        // Act
        AdministratorConfiguration.ReadWhatTheBinderCannotSay(section, [alice, auditor]);

        // Assert
        Assert.True(alice.GrantsTheWholeSurface);
        Assert.False(auditor.GrantsTheWholeSurface);
        Assert.Equal(
            [
                ($"{SectionName}:Administrators:0:Credentials:0", alice.Credentials[0]),
                ($"{SectionName}:Administrators:2:Credentials:3", auditor.Credentials[0]),
            ],
            AdministratorConfiguration.CredentialsWithPathsIn(SectionName, [alice, auditor]));
    }

    /// <summary>The pairing is positional, so a list of a different length is read as nothing rather than as somebody else's grant.</summary>
    [Fact]
    public void ReadWhatTheBinderCannotSay_MoreChildrenThanAdministrators_LeavesEveryGrantAtTheRestrictiveDefault()
    {
        // Arrange
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AdminEndpoint:Administrators:0:Name"] = "alice",
                ["AdminEndpoint:Administrators:1:Name"] = "bob",
            })
            .Build()
            .GetSection(SectionName);

        var alice = ConfiguredAuthentication.AdministratorWithApiKey("alice");

        // Act
        AdministratorConfiguration.ReadWhatTheBinderCannotSay(section, [alice]);

        // Assert
        Assert.False(alice.GrantsTheWholeSurface);
        Assert.Null(alice.ConfigurationKey);
    }

    /// <summary>Each key resolves to the administrator it is written under, which decides both the name an act is attributed to and the grant it holds.</summary>
    [Fact]
    public void AdministratorsByApiKeyName_TwoAdministratorsWithSeveralKeys_MapsEachKeyToItsOwnAdministrator()
    {
        // Arrange
        var alice = ConfiguredAuthentication.Administrator(
            "alice",
            ConfiguredAuthentication.ApiKey("alice-2026"),
            ConfiguredAuthentication.ApiKey("alice-2027"));
        var pipeline = ConfiguredAuthentication.Administrator(
            "ci-pipeline",
            ConfiguredAuthentication.ApiKey("pipeline"),
            ConfiguredAuthentication.PublicKey("pipeline-signing"));

        // Act
        var byApiKey = AdministratorConfiguration.AdministratorsByApiKeyName([alice, pipeline]);
        var byPublicKey = AdministratorConfiguration.AdministratorsByPublicKeyName([alice, pipeline]);

        // Assert
        Assert.Same(alice, byApiKey["alice-2026"]);
        Assert.Same(alice, byApiKey["alice-2027"]);
        Assert.Same(pipeline, byApiKey["pipeline"]);
        Assert.Same(pipeline, Assert.Single(byPublicKey).Value);
    }

    /// <summary>A token binds by the issuer and the subject together, and carries the scopes of the credential that named it.</summary>
    [Fact]
    public void TokenBindingsByIdentity_TwoAdministratorsAtOneServer_BindsEachSubjectToItsOwnAdministrator()
    {
        // Arrange
        var aliceCredential = ConfiguredAuthentication.OAuthFor(Resource, subject: "alice-subject");
        aliceCredential.OAuth!.RequiredScopes.Add("admin");
        var alice = ConfiguredAuthentication.Administrator("alice", aliceCredential);
        var bob = ConfiguredAuthentication.Administrator(
            "bob",
            ConfiguredAuthentication.OAuthFor(Resource, subject: " bob-subject "));

        // Act
        var bindings = AdministratorConfiguration.TokenBindingsByIdentity([alice, bob]);

        // Assert
        var aliceBinding = bindings[OAuthIdentity.IdentityOf(Issuer, "alice-subject")];
        Assert.Same(alice, aliceBinding.Administrator);
        Assert.Equal(["admin"], aliceBinding.RequiredScopes);

        var bobBinding = bindings[OAuthIdentity.IdentityOf(Issuer, "bob-subject")];
        Assert.Same(bob, bobBinding.Administrator);
        Assert.Empty(bobBinding.RequiredScopes);
    }

    [Fact]
    public void TokenBindingsByIdentity_AnAdministratorSigningInOnlyByKey_BindsNoToken()
    {
        // Arrange
        var alice = ConfiguredAuthentication.AdministratorWithApiKey("alice");

        // Act, Assert
        Assert.Empty(AdministratorConfiguration.TokenBindingsByIdentity([alice]));
        Assert.Empty(AdministratorConfiguration.OAuthMethodsIn([alice]));
        Assert.Equal(["alice"], AdministratorConfiguration.ApiKeysIn([alice]).Select(key => key.Name));
    }

    [Fact]
    public void FindConfigurationErrors_AnAdministratorGrantedOnlyWhatItsTokensCarry_ReportsNothing()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator("alice", ConfiguredAuthentication.OAuthFor(Resource));
        administrator.PermissionsFromTokenScopes = true;
        administrator.Permissions.Add(MailFathomPermission.AdminRead.Name);

        // Act, Assert
        Assert.Empty(AdministratorConfiguration.FindConfigurationErrors(SectionName, [administrator]));
    }
}
