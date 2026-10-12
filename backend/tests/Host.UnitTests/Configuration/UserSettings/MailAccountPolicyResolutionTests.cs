// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Reflection;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers what a mail account is served once the policies governing it are applied. Every reader of an account —
/// synchronization, delivery, the rules, classification, scanning, contact collection — binds the declaration these
/// tests bind, so what holds here holds for each of them.
/// </summary>
public sealed class MailAccountPolicyResolutionTests
{
    private const string NotAPolicy = """{"MailAccounts":{"Forced":{"Dialect":"Silesian"}}}""";

    private const string DeploymentPolicy =
        """{"MailAccounts":{"Defaults":{"Port":1993,"Mode":"Push","Folders":[{"Alias":"DEPLOYMENT","RemotePath":"Deployment"}]}}}""";

    private const string OrganizationPolicy =
        """{"MailAccounts":{"Forced":{"Host":"imap.corp.example.test"},"Defaults":{"Port":2993,"Folders":[{"Alias":"ORGANIZATION","RemotePath":"Organization"}]}}}""";

    private const string DeploymentForcingTheFolders =
        """{"MailAccounts":{"Forced":{"Folders":[{"Alias":"FORCED","RemotePath":"Forced"}]}}}""";

    private const string StatingItsOwnFolder = """
        "Folders": [ { "Alias": "OWN", "RemotePath": "Own" } ],
        """;

    /// <summary>
    /// Each property takes the first of: what the deployment forces, what the organization forces, what the account
    /// states, the organization's default, the deployment's default, and what the build supplies.
    /// </summary>
    [Fact]
    public void TryBind_AnAccountInAnOrganization_IsServedWhatTheLayersResolveInTheirOrder()
    {
        // Arrange
        var account = Mailbox(StatedPolicies.Governing(deployment: DeploymentPolicy, organization: OrganizationPolicy));

        // Act
        var served = MailAccountQueryableSettingsReading.TryBind(account);

        // Assert
        Assert.Equal("imap.corp.example.test", served!.Host);
        Assert.Equal(2993, served.Port);
        Assert.Equal("Push", served.Mode.ToString());
        Assert.Equal("work@example.test", served.UserName);
    }

    [Fact]
    public void TryBind_AnAccountInNoOrganization_IsServedTheDeploymentsPolicyAndNoOther()
    {
        // Arrange
        var account = Mailbox(StatedPolicies.Governing(deployment: DeploymentPolicy));

        // Act
        var served = MailAccountQueryableSettingsReading.TryBind(account);

        // Assert
        Assert.Equal("imap.example.test", served!.Host);
        Assert.Equal(1993, served.Port);
    }

    /// <summary>A list is one value: the layer that decides it supplies the whole of it, and no two layers are ever merged.</summary>
    [Theory]
    [InlineData(StatingItsOwnFolder, DeploymentPolicy, "OWN")]
    [InlineData("", DeploymentPolicy, "ORGANIZATION")]
    [InlineData(StatingItsOwnFolder, DeploymentForcingTheFolders, "FORCED")]
    public void TryBind_AListStatedAtMoreThanOneLayer_IsServedTheWholeListOfTheLayerThatDecidesIt(
        string statedFolders,
        string deploymentPolicy,
        string expected)
    {
        // Arrange
        var policies = StatedPolicies.Governing(deployment: deploymentPolicy, organization: OrganizationPolicy);

        // Act
        var served = MailAccountQueryableSettingsReading.TryBind(Mailbox(policies, statedFolders));

        // Assert
        Assert.Equal([expected], served!.Folders.Select(folder => folder.Alias));
    }

    /// <summary>The account's own document is what a lifted force brings back, so resolving it never changes it.</summary>
    [Fact]
    public void Of_AnAccountUnderPolicies_DerivesItsColumnsFromWhatItIsServedAndLeavesItsDocumentAsItWasStated()
    {
        // Arrange
        var account = Mailbox(StatedPolicies.Governing(deployment: DeploymentPolicy, organization: OrganizationPolicy));

        // Act
        var settings = MailAccountQueryableSettingsReading.Of(account);

        // Assert
        Assert.Equal("Push", settings!.SynchronizationMode.ToString());
        Assert.Equal(["ORGANIZATION"], settings.Folders.Select(folder => folder.Alias.ToString()));
        Assert.DoesNotContain("ORGANIZATION", account.Document, StringComparison.Ordinal);
    }

    /// <summary>
    /// An account governed by a policy this build does not read is one it cannot resolve, so it is bound as nothing —
    /// held back rather than served settings a policy may have meant to replace — and its columns are left alone.
    /// </summary>
    [Fact]
    public void TryBind_AnAccountGovernedByAPolicyThisBuildDoesNotRead_BindsNothingAndDerivesNoColumn()
    {
        // Arrange
        var account = Mailbox(StatedPolicies.Governing(organization: NotAPolicy));

        // Act
        var served = MailAccountQueryableSettingsReading.TryBind(account);
        var settings = MailAccountQueryableSettingsReading.Of(account);

        // Assert
        Assert.Null(served);
        Assert.Null(settings);
    }

    /// <summary>
    /// The name is a column of the account's row and the address another, so a document spelling either is not what
    /// says them: the row is, for the value a policy reports and for the declaration that is bound.
    /// </summary>
    [Fact]
    public void Resolve_AnAccountWhoseDocumentSpellsItsNameAndAddress_AnswersWhatItsRowHolds()
    {
        // Arrange
        var account = Mailbox(
            StatedPolicies.Governing(),
            """
            "DisplayName": "somebody else",
            "emailAddress": "elsewhere@example.test",
            """);

        // Act
        var resolved = MailAccountPolicyResolution.ResolveUnderItsPolicies(account)!;

        // Assert
        Assert.Equal("work", resolved.Find("DisplayName").EffectiveValue!.GetValue<string>());
        Assert.Equal("work@example.test", resolved.Find("EmailAddress").EffectiveValue!.GetValue<string>());
        Assert.DoesNotContain("somebody else", resolved.EffectiveJson, StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere@example.test", resolved.EffectiveJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every setting an account declares is either resolved through the policies — itself, or as the settings of the
    /// block it is — or marked as outside every policy with the reason. A property added to the declaration that is neither fails here, which is what keeps a new
    /// setting from being served straight off the account's document while every other follows the policy.
    /// </summary>
    [Fact]
    public void Resolve_EverySettingTheAccountDeclarationDeclares_IsGovernedOrMarkedOutsideEveryPolicy()
    {
        // Arrange
        var governable = SettingsPolicySection.MailAccounts.Properties;
        var declared = typeof(MailSynchronizationAccountOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToArray();

        // Act
        var resolved = MailAccountPolicyResolution.ResolveUnderItsPolicies(Mailbox(StatedPolicies.Governing()))!
            .Properties
            .Select(property => property.Property.Path)
            .ToHashSet(StringComparer.Ordinal);
        var ungoverned = declared.Where(name =>
            !resolved.Contains(name)
            && !resolved.Any(path => path.StartsWith($"{name}:", StringComparison.Ordinal))
            && governable.FindReasonOutside(name) is null);

        // Assert
        Assert.NotEmpty(declared);
        Assert.Empty(ungoverned);
        Assert.Equal(
            governable.All
                .Where(property => property.Shape != GovernablePropertyShape.Block)
                .Select(property => property.Path)
                .Order(StringComparer.Ordinal),
            resolved.Order(StringComparer.Ordinal));
    }

    private static MailAccountRecord Mailbox(GoverningSettingsPolicies policies, string stated = "") =>
        new(
            Guid.NewGuid(),
            "work@example.test",
            "work",
            $$"""
              {
                {{stated}}
                "Language": "English",
                "Host": "imap.example.test",
                "UserName": "work@example.test",
                "Secrets": { "Password": { "Name": "work-password", "SecretReference": "file:/run/secrets/work-password" } }
              }
              """,
            Version: 1)
        {
            Policies = policies,
        };
}
