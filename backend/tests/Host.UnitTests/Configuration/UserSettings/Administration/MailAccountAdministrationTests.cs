// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers what is done to the mail accounts a deployment holds. Two callers reach it — an administrator naming the
/// account and the user, and a user naming only their own account — so the cases that matter are the rules both hold:
/// that an address is held by one account in the whole deployment and a user is never told whose, that a display name
/// tells a user's accounts apart, that a user names only credentials provisioned for them, that ending the last
/// assignment erases the account, and that every committed write reaches the roster before it is announced.
/// </summary>
public sealed class MailAccountAdministrationTests
{
    /// <summary>A record of a user's language and nothing else, which is what a provisioning leaves behind.</summary>
    private const string LanguageOnlyRecord = """{"Language":"English"}""";

    private static readonly MailUserId Alex = SyntheticMailUser.Deployment;

    private static readonly MailUserId Sam = SyntheticMailUser.Another;

    [Fact]
    public async Task CreateAsync_ADeclarationTheUsersMailboxesAccept_CreatesTheAccountAndAssignsItToThem()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 4);

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration("archive@example.test", "archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(created!.Outcome.IsCommitted);
        var assigned = Assert.Single(deployment.MailAccountRecords.DocumentOf(Alex)!.MailAccounts);
        Assert.Equal(created.AccountId, assigned.Id);
        Assert.Equal("archive@example.test", assigned.EmailAddress);
        Assert.Equal(assigned.Version, created.Outcome.Version);
    }

    [Fact]
    public async Task CreateAsync_AUserThisDeploymentDoesNotHold_ReportsNothing()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration("archive@example.test", "archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(created);
    }

    /// <summary>An administrator told the address is held is told which, because finding the account that holds it is what they do next.</summary>
    [Fact]
    public async Task CreateAsync_AnAddressAnotherAccountHolds_IsRefusedNamingTheAddress()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Sam, LanguageOnlyRecord, version: 1, Mailbox("shared@example.test", "theirs"));
        deployment.Holding(Alex, LanguageOnlyRecord, version: 2);

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration("SHARED@example.test", "mine"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, created!.Outcome.Refusal);
        var refusal = Assert.Single(created.Outcome.Messages);
        Assert.Contains("'SHARED@example.test'", refusal, StringComparison.Ordinal);
        Assert.Single(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>A user is told the same sentence whoever holds the address, so nobody learns through this which addresses somebody else's mail arrives at.</summary>
    [Fact]
    public async Task AddOwnAsync_AnAddressAnotherAccountHolds_IsRefusedWithoutSayingWhoHoldsIt()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Sam, LanguageOnlyRecord, version: 1, Mailbox("shared@example.test", "theirs"));
        deployment.Holding(Alex, LanguageOnlyRecord, version: 2);

        // Act
        var outcome = await deployment.MailAccounts.AddOwnAsync(
            Declaration("shared@example.test", "mine", ProvisionedFor(Alex, "mine")),
            expectedVersion: 2,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "This mail account cannot be added for you. Ask whoever administers this deployment to add it.",
            Assert.Single(outcome!.Messages));
        Assert.Equal(2, outcome.Version);
        Assert.Single(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>The identifier is this deployment's to generate, so a declaration deciding one is refused before anything is judged.</summary>
    [Fact]
    public async Task CreateAsync_ADeclarationStatingAnIdentifier_IsRefusedAndCreatesNothing()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1);

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            """{"AccountId":"archive","EmailAddress":"archive@example.test","DisplayName":"archive","Host":"imap.example.test"}""",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, created!.Outcome.Refusal);
        Assert.Contains("AccountId", Assert.Single(created.Outcome.Messages), StringComparison.Ordinal);
        Assert.Empty(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>A display name is what tells one user's accounts apart, so an account carrying one they already answer to is refused.</summary>
    [Fact]
    public async Task CreateAsync_ADisplayNameAnotherOfTheUsersAccountsCarries_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, Mailbox("work@example.test", "work"));

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration("other@example.test", "work"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, created!.Outcome.Refusal);
        Assert.Contains(created.Outcome.Messages, message => message.Contains("'work'", StringComparison.Ordinal));
        Assert.Single(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>A reference outside this user's own material would hand one person another's credential to present to a mail server.</summary>
    [Fact]
    public async Task AddOwnAsync_AMailboxNamingAnotherUsersCredential_IsRefusedAndCreatesNothing()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await deployment.MailAccounts.AddOwnAsync(
            Declaration("archive@example.test", "archive", ProvisionedFor(Sam, "archive")),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Empty(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>A user's grant is not an administrator's, so their entry point refuses a caller holding only the administrative one.</summary>
    [Fact]
    public async Task AddOwnAsync_ACallerHoldingOnlyTheAdministrativeWrite_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite], Alex);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => deployment.MailAccounts.AddOwnAsync(
                Declaration("archive@example.test", "archive", ProvisionedFor(Alex, "archive")),
                expectedVersion: 1,
                TestContext.Current.CancellationToken));
    }

    /// <summary>An account is served to one user at a time, so one somebody already holds is not handed to a second person.</summary>
    [Fact]
    public async Task AssignAsync_AnAccountAnotherUserIsAssigned_IsRefusedAndWritesNothing()
    {
        // Arrange
        var shared = Mailbox("shared@example.test", "shared");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Sam, LanguageOnlyRecord, version: 1, shared);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 5);

        // Act
        var outcome = await deployment.MailAccounts.AssignAsync(shared.Id, Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Equal(
            "This mail account is already assigned to another user, and an account is served to one user at a time, so nothing was written.",
            Assert.Single(outcome.Messages));
        var alex = deployment.MailAccountRecords.DocumentOf(Alex)!;
        Assert.Empty(alex.MailAccounts);
        Assert.Equal(5, alex.Version);
    }

    /// <summary>An account nobody is assigned any longer is erased with it, because mail nobody is served is mail nobody asked to keep.</summary>
    [Fact]
    public async Task UnassignAsync_TheLastUserAssigned_ErasesTheAccount()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var unassignment = await deployment.MailAccounts.UnassignAsync(work.Id, Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new MailAccountUnassignment(Unassigned: true, AccountErased: true), unassignment);
        Assert.Empty(deployment.MailAccountRecords.Accounts);
    }

    [Fact]
    public async Task UnassignAsync_AnAccountSomebodyElseIsStillAssigned_KeepsIt()
    {
        // Arrange
        var shared = Mailbox("shared@example.test", "shared");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, shared);
        deployment.Holding(Sam, LanguageOnlyRecord, version: 1, shared);

        // Act
        var unassignment = await deployment.MailAccounts.UnassignAsync(shared.Id, Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new MailAccountUnassignment(Unassigned: true, AccountErased: false), unassignment);
        Assert.Equal([shared.Id], deployment.MailAccountRecords.DocumentOf(Sam)!.MailAccounts.Select(account => account.Id));
    }

    /// <summary>Ending an assignment can dispose of every message held for a mailbox, so it is published under the erasure grant rather than the configuration write.</summary>
    [Fact]
    public async Task UnassignAsync_ACallerHoldingOnlyTheConfigurationWrite_IsRefusedNamingTheErasureGrant()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => deployment.MailAccounts.UnassignAsync(work.Id, Alex, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
        Assert.Single(deployment.MailAccountRecords.Accounts);
    }

    /// <summary>A user composes every change over their own record's version, so the version a folder change answers with is the one their record moved to.</summary>
    [Fact]
    public async Task AddOwnFolderAsync_AFolderOnTheirOwnAccount_SavesItAndAnswersTheVersionTheirRecordMovedTo()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", ProvisionedFor(Alex, "work"));
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 3, work);

        // Act
        var outcome = await deployment.MailAccounts.AddOwnFolderAsync(
            work.Id.ToString("D"),
            """{"Alias":"INBOX/PROJECTS","RemotePath":"INBOX/Projects"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(4, outcome.Version);
        Assert.Contains("INBOX/PROJECTS", Assert.Single(deployment.MailAccountRecords.Accounts).Document, StringComparison.Ordinal);
    }

    /// <summary>An account somebody else is assigned is not one this user can reach by guessing its identifier.</summary>
    [Fact]
    public async Task AddOwnFolderAsync_AnAccountTheyAreNotAssigned_IsRefusedWithoutSavingIt()
    {
        // Arrange
        var theirs = Mailbox("sam@example.test", "sam");
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Sam, LanguageOnlyRecord, version: 1, theirs);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await deployment.MailAccounts.AddOwnFolderAsync(
            theirs.Id.ToString("D"),
            """{"Alias":"INBOX","RemotePath":"INBOX"}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("You are assigned no mail account", Assert.Single(outcome!.Messages), StringComparison.Ordinal);
        Assert.Equal(theirs, Assert.Single(deployment.MailAccountRecords.Accounts));
    }

    /// <summary>An account erased while the change was composed is gone rather than moved on, so no re-read could settle a conflict and the change answers that there is nothing to change.</summary>
    [Fact]
    public async Task AddOwnFolderAsync_AnAccountErasedBeforeTheSave_ReportsNothingRatherThanASupersededVersion()
    {
        // Arrange
        var erased = Mailbox("work@example.test", "work", ProvisionedFor(Alex, "work"));
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 3);
        deployment.Documents.ReadAsync(Alex, Arg.Any<CancellationToken>())
            .Returns(new UserSettingsDocument(Alex, "alex", LanguageOnlyRecord, 3) { MailAccounts = [erased] });

        // Act
        var outcome = await deployment.MailAccounts.AddOwnFolderAsync(
            erased.Id.ToString("D"),
            """{"Alias":"INBOX/PROJECTS","RemotePath":"INBOX/Projects"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome);
    }

    [Fact]
    public async Task ReplaceOwnFolderAsync_AnAliasTheAccountDoesNotDeclare_IsRefusedNamingIt()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", ProvisionedFor(Alex, "work"), folderAlias: "INBOX");
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var outcome = await deployment.MailAccounts.ReplaceOwnFolderAsync(
            work.Id.ToString("D"),
            "ARCHIVE",
            """{"Alias":"ARCHIVE","RemotePath":"Archive"}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("This mail account declares no folder 'ARCHIVE'.", Assert.Single(outcome!.Messages));
        Assert.Equal(work, Assert.Single(deployment.MailAccountRecords.Accounts));
    }

    [Fact]
    public async Task RemoveOwnFolderAsync_AFolderTheAccountDeclares_WithdrawsIt()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", ProvisionedFor(Alex, "work"), folderAlias: "INBOX/OLD");
        var deployment = new UserRecordDeployment([MailFathomPermission.MailAccountsWrite], Alex);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 2, work);

        // Act
        var outcome = await deployment.MailAccounts.RemoveOwnFolderAsync(
            work.Id.ToString("D"),
            "INBOX/OLD",
            expectedVersion: 2,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(3, outcome.Version);
        Assert.DoesNotContain("INBOX/OLD", Assert.Single(deployment.MailAccountRecords.Accounts).Document, StringComparison.Ordinal);
    }

    /// <summary>
    /// A committed account is served on this replica before it is announced, so a replica that hears the announcement
    /// finds this one's roster free rather than still being written, and this replica serves the account at once.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ACommittedAccount_ConvergesTheRosterBeforeAnnouncingIt()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 4);
        var heard = await RosterAnnouncementListener.ListenAsync(deployment.Backplane, deployment.ServedUsers);

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration("archive@example.test", "archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([true], heard);
        Assert.Equal(5, deployment.ServedUsers.PublishedVersionOf(Alex));
        Assert.Equal(
            [created!.AccountId!.Value.ToString("D")],
            Assert.Single(deployment.ServedUsers.Users).MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>The rows are committed whether or not this replica can read them back, so a reading that failed after the commit does not turn the write into a reported failure.</summary>
    [Fact]
    public async Task CreateAsync_TheRosterUnreadableAfterTheCommit_AnswersTheCommittedWrite()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 4);
        deployment.Documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserSettingsUnreadableException("The user records could not be read."));

        // Act
        var created = await deployment.MailAccounts.CreateAsync(
            Alex,
            Declaration("archive@example.test", "archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(created!.Outcome.IsCommitted);
        Assert.Equal(created.AccountId, Assert.Single(deployment.MailAccountRecords.Accounts).Id);
    }

    /// <summary>A listing cut at its bound says so, because an administrator reading it would otherwise take it as every account the deployment holds.</summary>
    [Fact]
    public async Task ReadAllAsync_MoreAccountsThanOneListingReads_ListsTheFirstAndSaysTheListingWasCut()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(
            Alex,
            LanguageOnlyRecord,
            version: 1,
            [.. Enumerable.Range(0, MailAccountAdministration.MaximumListed + 1)
                .Select(index => Mailbox($"box{index}@example.test", $"box{index}"))]);

        // Act
        var listing = await deployment.MailAccounts.ReadAllAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(listing.Truncated);
        Assert.Equal(MailAccountAdministration.MaximumListed, listing.Accounts.Count);
    }

    /// <summary>A refused account changed nothing, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task CreateAsync_ARefusedAccount_AnnouncesNothing()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 4);
        var heard = await RosterAnnouncementListener.ListenAsync(deployment.Backplane, deployment.ServedUsers);

        // Act
        await deployment.MailAccounts.CreateAsync(
            Alex,
            """{"EmailAddress":"archive@example.test"}""",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>What an administrator edits and hands back carries a reference the deployment resolves, so the reading replaces it.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountCarryingACredential_HandsOverTheDeclarationRedacted()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var reading = await deployment.MailAccounts.ReadAsync(work.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Alex], reading!.Users);
        Assert.Contains("work@example.test", reading.Declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("/run/secrets/work-password", reading.Declaration, StringComparison.Ordinal);
    }

    /// <summary>
    /// A saved declaration becomes keyed changes rather than replacing the account wholesale, so a value left at the
    /// redaction marker leaves the reference beneath it exactly as it was rather than persisting the marker over
    /// somebody's credential.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ADeclarationSavedWithTheCredentialLeftAtTheMarker_LeavesTheReferenceBeneathItUntouched()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite, MailFathomPermission.AdminRead]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);
        var reading = await deployment.MailAccounts.ReadAsync(work.Id, TestContext.Current.CancellationToken);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            reading!.Declaration.Replace("imap.example.test", "imap2.example.test", StringComparison.Ordinal),
            reading.Version,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        var saved = Assert.Single(deployment.MailAccountRecords.Accounts).Document;
        Assert.Contains("imap2.example.test", saved, StringComparison.Ordinal);
        Assert.Contains("/run/secrets/work-password", saved, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingRedaction.Marker, saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reference that already reached nothing before the edit is not what the edit is about, so an unrelated setting
    /// saved beside it commits and the problem is still reported — as one the account already carried.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AnUnrelatedSettingBesideAReferenceThatAlreadyReachedNothing_CommitsAndReportsItAsAlreadyHeld()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", $"file:{RegisteredSchemeSecretReferenceResolver.UnreadableTarget}/work-password");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            MailAccountDeclaration.Of(work).Replace("imap.example.test", "imap2.example.test", StringComparison.Ordinal),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Contains("already carried this before the change", Assert.Single(outcome.Messages), StringComparison.Ordinal);
    }

    /// <summary>
    /// The walk's sentence names a path and a failure, never the target, so a broken reference replaced by a different
    /// broken one reads the same — and is still this write's own problem, refused rather than committed as though the
    /// account already carried it.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ABrokenReferenceReplacedByAnotherBrokenOne_IsRefusedRatherThanReportedAsAlreadyHeld()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", $"file:{RegisteredSchemeSecretReferenceResolver.UnreadableTarget}/work-password");
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            MailAccountDeclaration.Of(work).Replace("/work-password", "/work-passwrod", StringComparison.Ordinal),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Equal(work, Assert.Single(deployment.MailAccountRecords.Accounts));
    }

    [Fact]
    public async Task SaveAsync_AVersionTheAccountHasMovedPast_IsRefusedReportingTheVersionInForce()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work") with { Version = 7 };
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(Alex, LanguageOnlyRecord, version: 1, work);

        // Act
        var outcome = await deployment.MailAccounts.SaveAsync(
            work.Id,
            MailAccountDeclaration.Of(work).Replace("imap.example.test", "imap2.example.test", StringComparison.Ordinal),
            expectedVersion: 6,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome!.Refusal);
        Assert.Equal(7, outcome.Version);
    }

    /// <summary>The address and the name a declaration states; its credential is read from where the secret name says unless a test names another reference.</summary>
    private static string Declaration(string emailAddress, string displayName, string? secretReference = null)
    {
        var secretName = emailAddress.Split('@')[0];

        return $$"""
                 {
                   "EmailAddress": "{{emailAddress}}",
                   "DisplayName": "{{displayName}}",
                   {{Settings(secretName, emailAddress, secretReference ?? $"file:/run/secrets/{secretName}-password")}}
                 }
                 """;
    }

    /// <summary>A mailbox already held, as its own record holds it.</summary>
    private static MailAccountRecord Mailbox(
        string emailAddress,
        string displayName,
        string? secretReference = null,
        string? folderAlias = null)
    {
        var secretName = emailAddress.Split('@')[0];
        var folders = folderAlias is null
            ? string.Empty
            : $$"""
                ,
                "Folders": [ { "Alias": "{{folderAlias}}", "RemotePath": "{{folderAlias}}" } ]
                """;

        return new MailAccountRecord(
            Guid.NewGuid(),
            emailAddress,
            displayName,
            $$"""
              {
                {{Settings(secretName, emailAddress, secretReference ?? $"file:/run/secrets/{secretName}-password")}}{{folders}}
              }
              """,
            Version: 1);
    }

    /// <summary>The settings every mailbox here is read with, without the braces around them.</summary>
    private static string Settings(string secretName, string userName, string secretReference) =>
        $$"""
          "Host": "imap.example.test",
          "UserName": "{{userName}}",
          "Secrets": { "Password": { "Name": "{{secretName}}-password", "SecretReference": "{{secretReference}}" } }
          """;

    /// <summary>A reference to material this deployment provisioned for one user, which the name it carries is what says.</summary>
    private static string ProvisionedFor(MailUserId user, string name) => $"file:/run/secrets/user-{user.Value:D}-{name}";
}
