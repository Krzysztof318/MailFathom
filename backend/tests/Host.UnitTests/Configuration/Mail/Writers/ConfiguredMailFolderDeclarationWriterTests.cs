// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail.Writers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Writers;

/// <summary>
/// Covers the one folder port that writes: that it writes into the account's own record under the folder grant rather
/// than the account grant, that it reads back only what that record declares, and that a rule the folder routes apply is
/// applied here too rather than gone around.
/// </summary>
public sealed class ConfiguredMailFolderDeclarationWriterTests
{
    /// <summary>The record a provisioning leaves behind, which declares nothing until its user asks for something.</summary>
    private const string EmptyRecord = "{}";

    private static readonly UserId Alex = SyntheticUser.Deployment;

    /// <summary>What the surface may act on is what this record declares, and an account it does not hold declares nothing.</summary>
    [Fact]
    public async Task AliasesTheAccountDeclaresAsync_AnAccountDeclaringAFolder_ReadsItsAliases()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "PROJECTS");
        var writer = WriterOver(work, out _);

        // Act
        var declared = await writer.AliasesTheAccountDeclaresAsync(AccountOf(work), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailFolderAlias.Create("PROJECTS")], declared);
    }

    [Fact]
    public async Task AliasesTheAccountDeclaresAsync_AnAccountTheUserDoesNotHold_ReadsNone()
    {
        // Arrange
        var writer = WriterOver(Mailbox("work@example.test", "work"), out _);

        // Act
        var declared = await writer.AliasesTheAccountDeclaresAsync(
            MailAccountId.Create(Guid.NewGuid().ToString()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(declared);
    }

    /// <summary>The version is read here rather than supplied, because the server has already made the folder by the time this is called.</summary>
    [Fact]
    public async Task DeclareAsync_AFolderTheMailServerHasMade_DeclaresItSynchronizedWithoutBeingToldAVersion()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.DeclareAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Archive/Projects", '/'),
            role: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);

        var folder = FolderIn(deployment, work, "0");

        Assert.Equal("PROJECTS", folder["Alias"]!.GetValue<string>());
        Assert.Equal("Archive/Projects", folder["RemotePath"]!.GetValue<string>());
        Assert.True(folder["Synchronize"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RepointAsync_AFolderTheAccountDeclares_MovesThePathAndLeavesTheAliasWhereItIs()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "PROJECTS");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.RepointAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Archive/PROJECTS", '/'),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);

        var folder = FolderIn(deployment, work, "0");

        Assert.Equal("PROJECTS", folder["Alias"]!.GetValue<string>());
        Assert.Equal("Archive/PROJECTS", folder["RemotePath"]!.GetValue<string>());
    }

    [Fact]
    public async Task RepointAsync_AFolderTheAccountDoesNotDeclare_IsRefusedAsMissing()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out _);

        // Act
        var outcome = await writer.RepointAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Projects", '/'),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.FolderMissing, outcome.Refusal);
    }

    [Fact]
    public async Task WithdrawAsync_AFolderTheAccountDeclares_TakesItOutOfWhatTheAccountReads()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "PROJECTS");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.WithdrawAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);
        Assert.False(AccountIn(deployment, work).ContainsKey("Folders"));
    }

    /// <summary>The folder an account files by is fixed wherever it is written, so this port is no way around that rule.</summary>
    [Fact]
    public async Task WithdrawAsync_AFolderPlayingARole_IsRefusedAndLeavesItDeclared()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "Trash", "Trash");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.WithdrawAsync(
            AccountOf(work),
            MailFolderAlias.Create("Trash"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.NotRecorded, outcome.Refusal);
        Assert.Equal(
            "Trash",
            AccountIn(deployment, work)["Folders"]!.AsArray()[0]!["Alias"]!.GetValue<string>());
    }

    /// <summary>The folder acts carry their own grant here, which is the one ADR 0034 allocated for them.</summary>
    [Fact]
    public async Task DeclareAsync_ACallerWithoutTheFolderGrant_IsRefused()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var deployment = new UserRecordDeployment([MailFathomPermission.MailRead], Alex);
        deployment.Holding(Alex, EmptyRecord, version: 1, work);
        var writer = new ConfiguredMailFolderDeclarationWriter(deployment.MailAccounts);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => writer.DeclareAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Projects", '/'),
            role: null,
            TestContext.Current.CancellationToken));
    }

    private static ConfiguredMailFolderDeclarationWriter WriterOver(MailAccountRecord account, out UserRecordDeployment deployment)
    {
        deployment = new UserRecordDeployment(
            [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite],
            Alex);
        deployment.Holding(Alex, EmptyRecord, version: 1, account);

        return new ConfiguredMailFolderDeclarationWriter(deployment.MailAccounts);
    }

    private static MailAccountId AccountOf(MailAccountRecord account) => MailAccountId.Create(account.Id.ToString());

    private static JsonObject AccountIn(UserRecordDeployment deployment, MailAccountRecord account) =>
        JsonNode.Parse(deployment.MailAccountRecords.Accounts.Single(held => held.Id == account.Id).Document)!.AsObject();

    private static JsonNode FolderIn(UserRecordDeployment deployment, MailAccountRecord account, string position) =>
        AccountIn(deployment, account)["Folders"]![position]!;

    /// <summary>A mailbox already held, optionally declaring one folder of its own.</summary>
    private static MailAccountRecord Mailbox(
        string emailAddress,
        string displayName,
        string? folderAlias = null,
        string? folderRole = null)
    {
        var secretName = emailAddress.Split('@')[0];
        var role = folderRole is null ? string.Empty : $$""", "SpecialUse": "{{folderRole}}" """;
        var folders = folderAlias is null
            ? string.Empty
            : $$"""
                ,
                "Folders": [ { "Alias": "{{folderAlias}}", "RemotePath": "{{folderAlias}}", "Synchronize": true, "CreateIfMissing": true{{role}} } ]
                """;

        return new MailAccountRecord(
            Guid.NewGuid(),
            emailAddress,
            displayName,
            $$"""
              {
                "Language": "English",
                "Host": "imap.example.test",
                "UserName": "{{emailAddress}}",
                "Secrets": { "Password": { "Name": "{{secretName}}-password", "SecretReference": "file:/run/secrets/{{secretName}}-password" } }{{folders}}
              }
              """,
            Version: 1);
    }
}
