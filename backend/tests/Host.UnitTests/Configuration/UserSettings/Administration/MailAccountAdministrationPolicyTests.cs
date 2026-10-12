// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers a mail account as the settings policies governing it decide it: what a write of it is refused and by whom,
/// what a declaration may leave to a default, which folders a folder change is composed over, which reading of the
/// policies a commit is judged under, and what a read reports per property.
/// </summary>
public sealed class MailAccountAdministrationPolicyTests
{
    private const string EmptyRecord = "{}";
    private const string CorporateHost = "imap.corp.example.test";
    private const string ForcingTheHost = """{"MailAccounts":{"Forced":{"Host":"imap.corp.example.test"}}}""";
    private const string WithholdingTheHost = """{"MailAccounts":{"Editing":{"Mode":"AllExcept","Properties":["Host"]}}}""";
    private const string WithholdingTheFolders = """{"MailAccounts":{"Editing":{"Mode":"AllExcept","Properties":["Folders"]}}}""";
    private const string NotAPolicy = """{"MailAccounts":{"Forced":{"Dialect":"Silesian"}}}""";

    private const string DefaultingTheServer =
        """{"MailAccounts":{"Defaults":{"Host":"imap.corp.example.test","Language":"English"}}}""";

    private const string DefaultingTheInbox =
        """{"MailAccounts":{"Defaults":{"Folders":[{"Alias":"INBOX","RemotePath":"INBOX"}]}}}""";

    private const string ForcingTheInbox =
        """{"MailAccounts":{"Forced":{"Folders":[{"Alias":"INBOX","RemotePath":"INBOX"}]}}}""";

    private const string DefaultingTheSecretScannerOff =
        """{"MailAccounts":{"Defaults":{"SensitiveContent":{"Secrets":{"Enabled":false}}}}}""";

    private const string ProjectsFolder = """{"Alias":"INBOX/PROJECTS","RemotePath":"INBOX/Projects"}""";

    private static readonly UserId Alex = SyntheticUser.Deployment;

    /// <summary>A declaration stating an address and a name, and leaving the server and the language to whoever supplies them.</summary>
    private static string WithoutAServer =>
        $$"""
          {
            "EmailAddress": "new@example.test",
            "DisplayName": "new",
            "UserName": "new@example.test",
            "Secrets": { "Password": { "Name": "new-password", "SecretReference": "{{ProvisionedFor("new")}}" } }
          }
          """;

    /// <summary>A forced value binds everybody, so an administrator stating another is refused naming the setting and who holds it.</summary>
    [Fact]
    public async Task SaveAsync_AnAdministratorMovingASettingAPolicyForces_IsRefusedNamingWhoHoldsIt()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(organization: ForcingTheHost), work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: "imap.elsewhere.example.test"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.True(outcome.IsHeld);
        Assert.StartsWith("Host is held by the organization", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(work.Document, Assert.Single(deployment.MailAccountRecords.Accounts).Document);
    }

    /// <summary>An editing restriction binds the person the account is assigned to and nobody administering it.</summary>
    [Fact]
    public async Task SaveAsync_AnAdministratorChangingASettingWithheldFromThePerson_Commits()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(deployment: WithholdingTheHost), work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: "imap.elsewhere.example.test"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Contains(
            "imap.elsewhere.example.test",
            Assert.Single(deployment.MailAccountRecords.Accounts).Document,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A caller that read the value in force and sends it back is changing nothing, so the save passes — and the
    /// account keeps the value somebody stated on it, which is what comes back when the force is lifted.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AForcedSettingCarriedThroughAsItIsServed_CommitsTheRestAndKeepsTheAccountsOwnValue()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(organization: ForcingTheHost), work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: CorporateHost).Replace("\"English\"", "\"Polish\"", StringComparison.Ordinal),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        var saved = Assert.Single(deployment.MailAccountRecords.Accounts).Document;
        Assert.Contains("Polish", saved, StringComparison.Ordinal);
        Assert.Contains("imap.example.test", saved, StringComparison.Ordinal);
        Assert.DoesNotContain(CorporateHost, saved, StringComparison.Ordinal);
    }

    /// <summary>A save whose every change is a held setting sent back as it is served writes nothing and moves no version.</summary>
    [Fact]
    public async Task SaveAsync_NothingButAForcedSettingCarriedThrough_ReportsNothingToChange()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(organization: ForcingTheHost), work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: CorporateHost),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome!.IsCommitted);
        Assert.True(outcome.IsSettled);
        Assert.Equal(1, Assert.Single(deployment.MailAccountRecords.Accounts).Version);
    }

    /// <summary>
    /// The policy a write is judged under is the one in force when it commits. One that came to force the setting
    /// after the account was read refuses the write inside the commit, which is the only place the two cannot pass
    /// each other.
    /// </summary>
    [Fact]
    public async Task SaveAsync_APolicyThatCameToForceTheSettingAfterTheAccountWasRead_IsRefusedByTheCommit()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(organization: ForcingTheHost), work);
        deployment.ReadingUnder(Alex, StatedPolicies.Governing(organization: "{}"));

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: "imap.elsewhere.example.test"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsHeld);
        Assert.StartsWith("Host is held by the organization", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(work.Document, Assert.Single(deployment.MailAccountRecords.Accounts).Document);
    }

    [Fact]
    public async Task SaveAsync_APolicyThisBuildDoesNotRead_IsHeldNamingThePolicyAndWritesNothing()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(organization: NotAPolicy), work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: "imap.elsewhere.example.test"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsHeld);
        Assert.StartsWith(
            "The settings policy of this record's organization",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        Assert.Equal(work.Document, Assert.Single(deployment.MailAccountRecords.Accounts).Document);
    }

    /// <summary>
    /// The rule that an account may switch a scanner on and never off is asked of the account as it is served, so a
    /// default switching one off under a deployment that requires it refuses the write of an account stating nothing
    /// about scanning at all.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AnAccountADefaultWouldSwitchARequiredScannerOffFor_IsRefusedNamingTheSetting()
    {
        // Arrange
        var scanning = new SensitiveContentOptions();
        scanning.Secrets.Enabled = true;

        var work = Mailbox();
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite], scanning: scanning);
        deployment.Holding(Alex, EmptyRecord, version: 1, work);
        deployment.GovernedBy(Alex, StatedPolicies.Governing(deployment: DefaultingTheSecretScannerOff));

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            DeclarationOf(work, host: "imap.elsewhere.example.test"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("SensitiveContent:Secrets:Enabled", Assert.Single(outcome.Messages), StringComparison.Ordinal);
    }

    /// <summary>
    /// A declaration states only what the policy does not supply. What a start requires of an account is asked of the
    /// account as it is served, so a server and a language a default supplies satisfy it, and the account's own
    /// document keeps what was stated and nothing a default lent it.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ADeclarationOmittingWhatADefaultSupplies_CommitsAndKeepsOnlyWhatWasStated()
    {
        // Arrange
        var policies = StatedPolicies.Governing(deployment: DefaultingTheServer);
        var deployment = Administered(policies);

        // Act
        var creation = await deployment.MailAccounts.CreateAsync(Alex, WithoutAServer, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(creation!.Outcome.IsCommitted);
        var created = Assert.Single(deployment.MailAccountRecords.Accounts);
        Assert.DoesNotContain(CorporateHost, created.Document, StringComparison.Ordinal);
        Assert.DoesNotContain("Language", created.Document, StringComparison.Ordinal);
        Assert.True(deployment.MailAccountRecords.Settings[created.Id].IsReadable);
        Assert.Equal(CorporateHost, MailAccountQueryableSettingsReading.TryBind(created with { Policies = policies })!.Host);
    }

    /// <summary>The control of the test above: the same declaration under no policy is one no start would serve.</summary>
    [Fact]
    public async Task CreateAsync_ADeclarationOmittingWhatNoPolicySupplies_IsRefusedAndCreatesNothing()
    {
        // Arrange
        var deployment = Administered(StatedPolicies.Governing());

        // Act
        var creation = await deployment.MailAccounts.CreateAsync(Alex, WithoutAServer, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, creation!.Outcome.Refusal);
        Assert.Empty(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>A forced value binds a declaration as it binds a save, so an account is never created stating another.</summary>
    [Fact]
    public async Task CreateAsync_ADeclarationStatingASettingAPolicyForcesOtherwise_IsRefusedNamingWhoHoldsIt()
    {
        // Arrange
        var deployment = Administered(StatedPolicies.Governing(deployment: ForcingTheHost));

        // Act
        var creation = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration(host: "imap.example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(creation!.Outcome.IsHeld);
        Assert.StartsWith("Host is held by the deployment", Assert.Single(creation.Outcome.Messages), StringComparison.Ordinal);
        Assert.Empty(deployment.MailAccountRecords.Accounts);
    }

    [Theory]
    [InlineData("add", WithholdingTheFolders)]
    [InlineData("replace", WithholdingTheFolders)]
    [InlineData("remove", WithholdingTheFolders)]
    [InlineData("add", ForcingTheInbox)]
    public async Task OwnFolderRoutes_FoldersAPolicyHolds_AreRefusedNamingWhoHoldsThem(string route, string organizationPolicy)
    {
        // Arrange
        var work = Mailbox(folderAlias: "INBOX/ARCHIVE");
        var deployment = SignedIn(StatedPolicies.Governing(organization: organizationPolicy), work);
        var accountId = work.Id.ToString("D");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var outcome = route switch
        {
            "add" => await deployment.MailAccounts.AddOwnFolderAsync(accountId, ProjectsFolder, expectedVersion: 1, cancellationToken),
            "replace" => await deployment.MailAccounts.ReplaceOwnFolderAsync(
                accountId,
                "INBOX/ARCHIVE",
                """{"Alias":"INBOX/ARCHIVE","RemotePath":"INBOX/Kept"}""",
                expectedVersion: 1,
                cancellationToken),
            _ => await deployment.MailAccounts.RemoveOwnFolderAsync(accountId, "INBOX/ARCHIVE", expectedVersion: 1, cancellationToken),
        };

        // Assert
        Assert.True(outcome!.IsHeld);
        Assert.StartsWith("Folders is held by the organization", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        Assert.Equal(work.Document, Assert.Single(deployment.MailAccountRecords.Accounts).Document);
    }

    /// <summary>
    /// A list is one value across the layers of a policy. An account stating no folders is served a default's, so a
    /// folder added to it states the whole list — the default's folders and the new one — rather than a list holding
    /// the new folder alone.
    /// </summary>
    [Fact]
    public async Task AddOwnFolderAsync_AnAccountServedTheFoldersOfADefault_StatesTheWholeListWithTheNewFolder()
    {
        // Arrange
        var work = Mailbox();
        var deployment = SignedIn(StatedPolicies.Governing(deployment: DefaultingTheInbox), work);

        // Act
        var outcome = await deployment.MailAccounts.AddOwnFolderAsync(
            work.Id.ToString("D"),
            ProjectsFolder,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        var aliases = deployment.MailAccountRecords.Settings[work.Id].Folders.Select(folder => folder.Alias).ToArray();
        Assert.Equal(2, aliases.Length);
        Assert.Contains(MailFolderAlias.Create("INBOX"), aliases);
        Assert.Contains(MailFolderAlias.Create("INBOX/PROJECTS"), aliases);
    }

    [Theory]
    [InlineData(DefaultingTheInbox, "INBOX")]
    [InlineData(ForcingTheInbox, null)]
    [InlineData(WithholdingTheFolders, null)]
    [InlineData(NotAPolicy, null)]
    public async Task AliasesOwnAccountDeclaresAsync_AnAccountStatingNoFolders_AnswersTheOnesTheUserMayStateAfresh(
        string deploymentPolicy,
        string? expected)
    {
        // Arrange
        var work = Mailbox();
        var deployment = SignedIn(StatedPolicies.Governing(deployment: deploymentPolicy), work);

        // Act
        var aliases = await deployment.MailAccounts.AliasesOwnAccountDeclaresAsync(
            work.Id.ToString("D"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected is null ? [] : [expected], aliases);
    }

    /// <summary>
    /// A read answers the account's own declaration, because that is what a save is composed over, and beside it each
    /// property as the policies resolve it: the value in force, the step that supplied it, and who holds it.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnAccountUnderPolicies_ReportsEachPropertyAsResolvedBesideItsOwnDeclaration()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(
            StatedPolicies.Governing(deployment: DefaultingTheInbox, organization: ForcingTheHost),
            work);

        // Act
        var reading = await deployment.MailAccounts.ReadAsync(work.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("imap.example.test", reading!.Declaration, StringComparison.Ordinal);
        Assert.Null(reading.Policy.Unresolved);

        var host = PropertyOf(reading.Policy, "Host");
        Assert.Equal(SettingsValueSource.OrganizationForced, host.Source);
        Assert.Equal(CorporateHost, host.EffectiveValue!.GetValue<string>());
        Assert.Equal(SettingsPolicyScope.Organization, host.HeldBy);
        Assert.DoesNotContain("Host", reading.Policy.Changeable);

        Assert.Equal(SettingsValueSource.DeploymentDefault, PropertyOf(reading.Policy, "Folders").Source);
        Assert.Equal(SettingsValueSource.Record, PropertyOf(reading.Policy, "Language").Source);
        Assert.Equal("work@example.test", PropertyOf(reading.Policy, "EmailAddress").EffectiveValue!.GetValue<string>());
        Assert.Equal("work", PropertyOf(reading.Policy, "DisplayName").EffectiveValue!.GetValue<string>());
        Assert.Contains("Folders", reading.Policy.Changeable);
        Assert.Contains("Language", reading.Policy.Changeable);
    }

    [Fact]
    public async Task ReadAsync_APolicyThisBuildDoesNotRead_ReportsNoPropertyAndSaysWhy()
    {
        // Arrange
        var work = Mailbox();
        var deployment = Administered(StatedPolicies.Governing(organization: NotAPolicy), work);

        // Act
        var reading = await deployment.MailAccounts.ReadAsync(work.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(reading!.Policy.Properties);
        Assert.Empty(reading.Policy.Changeable);
        Assert.StartsWith(
            "The settings policy of this record's organization",
            reading.Policy.Unresolved,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A person changes an account through the folder routes alone, so the list of folders is the one property of it
    /// that can be theirs.
    /// </summary>
    [Fact]
    public async Task ReadOwnAccountPoliciesAsync_AnAccountUnderPolicies_SaysOnlyItsFoldersAreThePersonsToChange()
    {
        // Arrange
        var work = Mailbox();
        var deployment = SignedIn(StatedPolicies.Governing(deployment: WithholdingTheHost, organization: DefaultingTheInbox), work);

        // Act
        var reading = await deployment.MailAccounts.ReadOwnAccountPoliciesAsync(TestContext.Current.CancellationToken);

        // Assert
        var held = Assert.Single(reading!);
        Assert.Equal(work.Id, held.Key);
        Assert.Equal(["Folders"], held.Value.Changeable);
        Assert.Equal(SettingsValueSource.OrganizationDefault, PropertyOf(held.Value, "Folders").Source);
        Assert.Equal(SettingsPolicyScope.Deployment, PropertyOf(held.Value, "Host").HeldBy);
    }

    /// <summary>A reader who may not write an account is offered no control whose change would be refused.</summary>
    [Fact]
    public async Task ReadOwnAccountPoliciesAsync_AReaderWithoutTheGrantToWriteAnAccount_IsToldNothingIsTheirsToChange()
    {
        // Arrange
        var work = Mailbox();
        var deployment = new UserRecordDeployment([MailFathomPermission.MailRead], actingFor: Alex);
        deployment.Holding(Alex, EmptyRecord, version: 1, work);

        // Act
        var reading = await deployment.MailAccounts.ReadOwnAccountPoliciesAsync(TestContext.Current.CancellationToken);

        // Assert
        var held = Assert.Single(reading!).Value;
        Assert.Empty(held.Changeable);
        Assert.NotEmpty(held.Properties);
    }

    private static ResolvedProperty PropertyOf(MailAccountPolicyReading reading, string path) =>
        reading.Properties.Single(property => property.Property.Path == path);

    private static UserRecordDeployment Administered(GoverningSettingsPolicies policies, params MailAccountRecord[] accounts) =>
        Holding(
            new UserRecordDeployment([MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite]),
            policies,
            accounts);

    private static UserRecordDeployment SignedIn(GoverningSettingsPolicies policies, params MailAccountRecord[] accounts) =>
        Holding(
            new UserRecordDeployment(
                [MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite],
                actingFor: Alex),
            policies,
            accounts);

    private static UserRecordDeployment Holding(
        UserRecordDeployment deployment,
        GoverningSettingsPolicies policies,
        MailAccountRecord[] accounts)
    {
        deployment.Holding(Alex, EmptyRecord, version: 1, accounts);
        deployment.GovernedBy(Alex, policies);

        return deployment;
    }

    /// <summary>The declaration of an account already held, with the server it states replaced.</summary>
    private static string DeclarationOf(MailAccountRecord account, string host) =>
        MailAccountDeclaration.Of(account).Replace("imap.example.test", host, StringComparison.Ordinal);

    /// <summary>The declaration of a mailbox being created.</summary>
    private static string Declaration(string host) =>
        $$"""
          {
            "EmailAddress": "new@example.test",
            "DisplayName": "new",
            "Language": "English",
            "Host": "{{host}}",
            "UserName": "new@example.test",
            "Secrets": { "Password": { "Name": "new-password", "SecretReference": "{{ProvisionedFor("new")}}" } }
          }
          """;

    /// <summary>A mailbox already held, as its own record holds it.</summary>
    private static MailAccountRecord Mailbox(string? folderAlias = null)
    {
        var folders = folderAlias is null
            ? string.Empty
            : $$"""
                ,
                "Folders": [ { "Alias": "{{folderAlias}}", "RemotePath": "{{folderAlias}}" } ]
                """;

        return new MailAccountRecord(
            Guid.NewGuid(),
            "work@example.test",
            "work",
            $$"""
              {
                "Language": "English",
                "Host": "imap.example.test",
                "UserName": "work@example.test",
                "Secrets": { "Password": { "Name": "work-password", "SecretReference": "{{ProvisionedFor("work")}}" } }{{folders}}
              }
              """,
            Version: 1);
    }

    /// <summary>A reference to material this deployment provisioned for the user these tests act for.</summary>
    private static string ProvisionedFor(string name) => $"file:/run/secrets/user-{Alex.Value:D}-{name}";
}
