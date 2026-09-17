// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Editing;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>
/// Covers the commands that create, read, edit, share, and erase a mail account. What these hold is the part of each act
/// that lives in the command: which user a creation is for, the version a save is composed over, the confirmation the
/// two destructive acts ask for, and what a refusal tells an operator.
/// </summary>
public sealed class MailAccountCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    /// <summary>The code a deployment refuses an address another account already holds with.</summary>
    private const int AddressHeld = 12050;

    private static readonly Guid User = FakeMailAccountDeployment.User;

    private static readonly Guid Account = FakeMailAccountDeployment.Account;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Where a declaration this suite hands to <c>--from-file</c> is written, cleaned up with the suite.</summary>
    private readonly string declarations =
        Path.Combine(Path.GetTempPath(), $"mailfathom-account-tests-{Guid.NewGuid():N}");

    /// <summary>An operator tells accounts apart by display name and address, so the listing prints both beside who holds the account.</summary>
    [Fact]
    public async Task List_ADeploymentHoldingOneAccount_PrintsItsNameAddressAndUsers()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"{Account:D}  Work <alex@example.test>", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"assigned to: {User:D}", StringComparison.Ordinal));
    }

    /// <summary>A listing the deployment cut at its bound says so, so an operator does not take it for every account held.</summary>
    [Fact]
    public async Task List_ADeploymentHoldingMoreThanOneListingCarries_SaysOnlyTheFirstAreListed()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.HoldingMoreThanOneListing();

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            "This deployment holds more than 1 mail accounts; only the first 1 are listed.",
            this.harness.Console.Lines);
    }

    [Fact]
    public async Task Show_AnAccountTheDeploymentHolds_PrintsItsDeclaration()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "show", "--account", $"{Account:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(FakeMailAccountDeployment.Declaration, this.harness.Console.Lines);
    }

    /// <summary>The identifier is the deployment's to generate, and a deployment holding one user needs nobody named.</summary>
    [Fact]
    public async Task Add_ADeclarationInAFile_CreatesItForTheResolvedUserAndReportsTheIdentifier()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();
        var declaration = await this.WriteDeclarationAsync("""{"EmailAddress":"archive@example.test","DisplayName":"Archive"}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "add", "--from-file", declaration, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var created = Assert.Single(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountsPath));
        var body = JsonDocument.Parse(created.ContentAsUtf8String()).RootElement;

        Assert.Equal(User, body.GetProperty("userId").GetGuid());
        Assert.Contains("archive@example.test", body.GetProperty("account").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"{FakeMailAccountDeployment.CreatedAccount:D}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_AnAddressAnotherAccountHolds_ReportsTheRefusalAndFails()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.RefusingTheWrite(
            AddressHeld,
            "Another mail account already holds 'archive@example.test'.");
        var declaration = await this.WriteDeclarationAsync("""{"EmailAddress":"archive@example.test","DisplayName":"Archive"}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "add", "--from-file", declaration, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("already holds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_APathNothingIsAt_SaysSoWithoutReachingTheDeployment()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account",
            "add",
            "--from-file",
            Path.Combine(Path.GetTempPath(), $"mailfathom-absent-{Guid.NewGuid():N}.json"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountsPath));
    }

    [Fact]
    public async Task Add_AnEmptyFile_SaysItDeclaresNoMailAccount()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();
        var declaration = await this.WriteDeclarationAsync("   ");

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "add", "--from-file", declaration, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("declares no mail account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Assign_AUserNamed_SendsThatUser()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account",
            "assign",
            "--account",
            $"{Account:D}",
            "--user",
            $"{User:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var assignment = Assert.Single(
            deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountAssignmentsPath(Account)));

        Assert.Equal(User, JsonDocument.Parse(assignment.ContentAsUtf8String()).RootElement.GetProperty("userId").GetGuid());
    }

    /// <summary>Ending an assignment erases mail nothing here restores, so it is never performed on an exhausted pipe.</summary>
    [Fact]
    public async Task Unassign_NoAgreementStatedAndNobodyAtTheTerminal_ErasesNothing()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "unassign", "--account", $"{Account:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountAssignmentRemovalPath(Account)));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("Nothing was erased", StringComparison.Ordinal));
    }

    /// <summary>The last assignment takes the account with it, and the operator is told so rather than left to find it gone.</summary>
    [Fact]
    public async Task Unassign_TheLastAssignmentConfirmed_SaysTheAccountWasErased()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.ErasingOnTheLastUnassignment();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account",
            "unassign",
            "--account",
            $"{Account:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountAssignmentRemovalPath(Account)));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("erased the account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_NoAgreementStatedAndNobodyAtTheTerminal_ErasesNothing()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "delete", "--account", $"{Account:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Delete, AdminEndpointRoutes.MailAccountPath(Account)));
    }

    [Fact]
    public async Task Delete_AConfirmedErasure_ErasesTheAccount()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account",
            "delete",
            "--account",
            $"{Account:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.UserRequestsTo(HttpMethod.Delete, AdminEndpointRoutes.MailAccountPath(Account)));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Erased mail account", StringComparison.Ordinal));
    }

    /// <summary>What the operator saved is what is committed, over the version the buffer was opened at.</summary>
    [Fact]
    public async Task Edit_AnEditedDeclaration_SavesWhatWasSavedOverTheVersionItWasOpenedAt()
    {
        // Arrange
        const string edited = """{"EmailAddress":"alex@example.test","DisplayName":"Personal"}""";

        using var deployment = FakeMailAccountDeployment.Holding();

        this.harness.EditsTheBufferInto(edited);

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "edit", "--account", $"{Account:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var saved = Assert.Single(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountPath(Account)));
        var body = JsonDocument.Parse(saved.ContentAsUtf8String()).RootElement;

        Assert.Equal(FakeMailAccountDeployment.AccountVersion, body.GetProperty("version").GetInt64());
        Assert.Equal(edited, body.GetProperty("account").GetString());
    }

    /// <summary>A declaration is edited through the same YAML view a user's record is, and saved as JSON.</summary>
    [Fact]
    public async Task Edit_AYamlView_SavesTheEditAsJson()
    {
        // Arrange
        using var deployment = FakeMailAccountDeployment.Holding();

        this.harness.EditsTheBufferInto("EmailAddress: alex@example.test\nDisplayName: Personal\n");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account",
            "edit",
            "--account",
            $"{Account:D}",
            "--format",
            "yaml",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var saved = Assert.Single(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.MailAccountPath(Account)));
        var body = JsonDocument.Parse(saved.ContentAsUtf8String()).RootElement;

        Assert.True(YamlDocumentView.DescribeTheSameDocument(
            """{"EmailAddress":"alex@example.test","DisplayName":"Personal"}""",
            body.GetProperty("account").GetString()!));
    }

    public void Dispose()
    {
        this.harness.Dispose();

        if (Directory.Exists(this.declarations))
        {
            Directory.Delete(this.declarations, recursive: true);
        }
    }

    private async Task<string> WriteDeclarationAsync(string declaration)
    {
        var path = Path.Combine(this.declarations, $"mail-account-{Guid.NewGuid():N}.json");

        Directory.CreateDirectory(this.declarations);
        await File.WriteAllTextAsync(path, declaration, TestContext.Current.CancellationToken);

        return path;
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
