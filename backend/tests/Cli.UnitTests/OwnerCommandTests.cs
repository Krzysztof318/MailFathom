// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>
/// Covers the commands that record an owner, list them, maintain their mailboxes, adopt them out of configuration, and
/// erase them. What these hold is the part of each act that lives in the command rather than in the deployment: the
/// version a write is composed over, the confirmation the two destructive acts ask for, and what a refusal tells an
/// operator to do next.
/// </summary>
public sealed class OwnerCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    /// <summary>The code a deployment refuses a write to a configuration-served owner with.</summary>
    private const int RecordReadFromConfiguration = 12015;

    private static readonly Guid Owner = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnotherOwner = new("22222222-2222-2222-2222-222222222222");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Where a declaration this suite hands to <c>--from-file</c> is written, cleaned up with the suite.</summary>
    private readonly string declarations =
        Path.Combine(Path.GetTempPath(), $"mailfathom-owner-tests-{Guid.NewGuid():N}");

    /// <summary>The identifier is the deployment's to mint, so what comes back is the one thing a script cannot reconstruct from what it typed.</summary>
    [Fact]
    public async Task Add_ALabelTheDeploymentAccepts_ReportsTheIdentifierItMinted()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.HoldingNobody();

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "add", "--display-name", "alex", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var recording = Assert.Single(deployment.OwnerRequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnersPath));

        Assert.Equal("alex", ReadField(recording.ContentAsUtf8String(), "displayName"));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("55555555", StringComparison.Ordinal));
    }

    /// <summary>A new owner's mailboxes are their own record's from the first moment, so the operator is told where to declare one.</summary>
    [Fact]
    public async Task Add_ARecordedOwner_SaysWhereTheirMailAccountsAreDeclared()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.HoldingNobody();

        // Act
        await this.RunAsync(deployment, "owner", "add", "--display-name", "alex", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("mfctl owner account add", StringComparison.Ordinal));
    }

    /// <summary>A label is the operator's own text and nothing is keyed by it, so a rename asks nothing and reports what the owner now carries.</summary>
    [Fact]
    public async Task Rename_AnOwnerTheDeploymentHolds_SendsTheLabelAndReportsIt()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "rename",
            "--owner",
            $"{Owner:D}",
            "--display-name",
            "alexandra",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var rename = Assert.Single(
            deployment.OwnerRequestsTo(HttpMethod.Put, AdminEndpointRoutes.OwnerDisplayNamePath(Owner)));

        Assert.Equal("alexandra", ReadField(rename.ContentAsUtf8String(), "displayName"));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("alexandra", StringComparison.Ordinal));
    }

    /// <summary>
    /// A start reads a declared owner's label from the file it is declared in, so a rename of one lasts until the next
    /// restart. Reporting the new label without saying so would report a change the deployment undoes.
    /// </summary>
    [Fact]
    public async Task Rename_AnOwnerAConfigurationSourceDeclares_SaysTheLabelLastsUntilARestart()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.SupplyingFromConfiguration(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "rename",
            "--owner",
            $"{Owner:D}",
            "--display-name",
            "alexandra",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("until the deployment is restarted", StringComparison.Ordinal));
    }

    /// <summary>An owner nothing declares keeps the label a rename writes, so nothing qualifies what the command reported.</summary>
    [Fact]
    public async Task Rename_AnOwnerNoConfigurationSourceDeclares_ReportsTheLabelWithNothingQualifyingIt()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        await this.RunAsync(
            deployment,
            "owner",
            "rename",
            "--owner",
            $"{Owner:D}",
            "--display-name",
            "alexandra",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.DoesNotContain(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("until the deployment is restarted", StringComparison.Ordinal));
    }

    /// <summary>A deployment holding one person is the ordinary shape, so the roster is what settles who a rename acts for.</summary>
    [Fact]
    public async Task Rename_NoOwnerNamedOnADeploymentHoldingOne_ActsForTheSingleOwnerItHolds()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "rename",
            "--display-name",
            "alexandra",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.OwnerRequestsTo(HttpMethod.Put, AdminEndpointRoutes.OwnerDisplayNamePath(Owner)));
    }

    /// <summary>The listing is where the two states that decide what to do next are read.</summary>
    [Fact]
    public async Task List_AnOwnerServedFromConfiguration_SaysTheAdoptionIsWhatMovesThem()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.SupplyingFromConfiguration(Owner);

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("mfctl owner adopt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_AnOwnerReadingTheirOwnRecord_SaysWhereTheirMailAccountsAreMaintained()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        await this.RunAsync(deployment, "owner", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("their own record", StringComparison.Ordinal));
    }

    /// <summary>An empty roster is a deployment that has not started successfully, which is worth saying rather than printing nothing.</summary>
    [Fact]
    public async Task List_ADeploymentHoldingNoOwner_SaysWhatAnEmptyRosterMeans()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.HoldingNobody();

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("has not started successfully", StringComparison.Ordinal));
    }

    /// <summary>A deployment serving one owner needs no identifier typed, which is what makes the ordinary invocation short.</summary>
    [Fact]
    public async Task Show_ADeploymentHoldingOneOwner_ResolvesThemWithoutAnIdentifierBeingTyped()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "show", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.OwnerRequestsTo(HttpMethod.Get, AdminEndpointRoutes.OwnerRecordPath(Owner)));
    }

    /// <summary>Composing a caller against whichever owner came first is how one person is handed another's mail, so the command refuses to guess.</summary>
    [Fact]
    public async Task Show_ADeploymentHoldingSeveralOwners_RefusesToGuessWhichOneWasMeant()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner, AnotherOwner);

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "show", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.OwnerRequestsTo(HttpMethod.Get, AdminEndpointRoutes.OwnerRecordPath(Owner)));
    }

    /// <summary>A record whose mailboxes are in a file is empty, and reading that without being told why looks like an owner with no mailboxes.</summary>
    [Fact]
    public async Task Show_AnOwnerServedFromConfiguration_SaysWhyTheRecordIsEmpty()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.SupplyingFromConfiguration(Owner);

        // Act
        await this.RunAsync(deployment, "owner", "show", "--owner", $"{Owner:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("mfctl owner adopt", StringComparison.Ordinal));
    }

    /// <summary>
    /// The record is read first so the write is composed over the version it was read at, which is what makes two
    /// administrators declaring a mailbox at once produce a refusal rather than one silently dropping the other's.
    /// </summary>
    [Fact]
    public async Task AccountAdd_ADeclarationInAFile_ComposesTheWriteOverTheVersionItRead()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);
        var declaration = await this.WriteDeclarationAsync("""{"AccountId":"archive"}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "account",
            "add",
            "--from-file",
            declaration,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var written = Assert.Single(
            deployment.OwnerRequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerMailAccountsPath(Owner)));

        Assert.Equal(FakeOwnerRecordDeployment.RecordVersion, ReadVersion(written.ContentAsUtf8String()));
        Assert.Contains("archive", ReadField(written.ContentAsUtf8String(), "account"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountAdd_APathNothingIsAt_SaysSoWithoutReachingTheDeployment()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "account",
            "add",
            "--from-file",
            Path.Combine(Path.GetTempPath(), $"mailfathom-absent-{Guid.NewGuid():N}.json"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.OwnerRequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerMailAccountsPath(Owner)));
    }

    [Fact]
    public async Task AccountAdd_AnEmptyFile_SaysItDeclaresNoMailAccount()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);
        var declaration = await this.WriteDeclarationAsync("   ");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "account",
            "add",
            "--from-file",
            declaration,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("declares no mail account", StringComparison.Ordinal));
    }

    /// <summary>The one refusal a command can repair names the repair, which is the adoption that moves the owner out of the files.</summary>
    [Fact]
    public async Task AccountAdd_AnOwnerAConfigurationSourceSupplies_NamesTheAdoptionAsTheRepair()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.RefusingTheWrite(
            Owner,
            RecordReadFromConfiguration,
            "This owner's mail accounts are supplied by a configuration source.");

        var declaration = await this.WriteDeclarationAsync("""{"AccountId":"archive"}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "account",
            "add",
            "--from-file",
            declaration,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("mfctl owner adopt", StringComparison.Ordinal));
    }

    /// <summary>No configuration change takes somebody's mail away, so a withdrawal says what it did not do.</summary>
    [Fact]
    public async Task AccountRemove_AnIdentifierTheRecordDeclares_SaysTheStoredMailWasNotTouched()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "account",
            "remove",
            "--id",
            "archive",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("was not touched", StringComparison.Ordinal));
    }

    /// <summary>An owner already reading their own record has nothing to move, and saying so is not a refusal.</summary>
    [Fact]
    public async Task Adopt_AnOwnerAlreadyReadingTheirOwnRecord_SaysThereIsNothingToAdopt()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "adopt", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.OwnerRequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerAdoptionPath(Owner)));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("nothing to adopt", StringComparison.Ordinal));
    }

    /// <summary>The preview names the mailboxes and the path behind them, which is the moment to notice it covers more than was meant.</summary>
    [Fact]
    public async Task Adopt_AnOwnerAConfigurationSourceSupplies_PreviewsTheMailboxesAndThePathBehindThem()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.SupplyingFromConfiguration(Owner, "primary", "archive");

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "adopt", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("primary", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("MailSynchronization:Accounts", StringComparison.Ordinal));
    }

    /// <summary>The adoption is composed over the version the preview reported, which is what the deployment accepts it against.</summary>
    [Fact]
    public async Task Adopt_AnOwnerAConfigurationSourceSupplies_ComposesTheAdoptionOverThePreviewedVersion()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.SupplyingFromConfiguration(Owner, "primary");

        // Act
        await this.RunAsync(deployment, "owner", "adopt", "--yes", "--endpoint", Endpoint);

        // Assert
        var adoption = Assert.Single(
            deployment.OwnerRequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerAdoptionPath(Owner)));

        Assert.Equal(FakeOwnerRecordDeployment.RecordVersion, ReadVersion(adoption.ContentAsUtf8String()));
    }

    /// <summary>Nobody is at the terminal, so an unconfirmed adoption is refused with the flag that states the agreement in the command.</summary>
    [Fact]
    public async Task Adopt_NoAgreementStatedAndNobodyAtTheTerminal_AdoptsNothing()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.SupplyingFromConfiguration(Owner, "primary");

        // Act
        var exitCode = await this.RunAsync(deployment, "owner", "adopt", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.OwnerRequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerAdoptionPath(Owner)));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("Nothing was adopted", StringComparison.Ordinal));
    }

    /// <summary>An identifier copied out of the wrong listing looks the same either way, so the confirmation names the person.</summary>
    [Fact]
    public async Task Remove_AConfirmedErasure_NamesThePersonRatherThanOnlyTheIdentifier()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "remove",
            "--owner",
            $"{Owner:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains($"owner-{Owner:D}", StringComparison.Ordinal));
    }

    /// <summary>Nothing here undoes an erasure, so it is never performed on an exhausted pipe.</summary>
    [Fact]
    public async Task Remove_NoAgreementStatedAndNobodyAtTheTerminal_ErasesNothing()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "remove",
            "--owner",
            $"{Owner:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.OwnerRequestsTo(HttpMethod.Delete, AdminEndpointRoutes.OwnerPath(Owner)));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("Nothing was erased", StringComparison.Ordinal));
    }

    /// <summary>A process serving an erased owner follows the erasure without asking for a restart.</summary>
    [Fact]
    public async Task Remove_AnErasedOwnerTheProcessWasServing_SaysNoRestartIsNeeded()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        await this.RunAsync(deployment, "owner", "remove", "--owner", $"{Owner:D}", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.DoesNotContain(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("Restart it", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("has stopped serving this owner", StringComparison.Ordinal));
    }

    /// <summary>An owner the deployment does not hold is nothing to erase rather than a failure, and the confirmation is never reached.</summary>
    [Fact]
    public async Task Remove_AnOwnerTheDeploymentDoesNotHold_SaysThereIsNothingToErase()
    {
        // Arrange
        using var deployment = FakeOwnerRecordDeployment.Holding(Owner);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "owner",
            "remove",
            "--owner",
            $"{AnotherOwner:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.OwnerRequestsTo(HttpMethod.Delete, AdminEndpointRoutes.OwnerPath(AnotherOwner)));
    }

    public void Dispose()
    {
        this.harness.Dispose();

        if (Directory.Exists(this.declarations))
        {
            Directory.Delete(this.declarations, recursive: true);
        }
    }

    private static string ReadField(string body, string name) =>
        JsonDocument.Parse(body).RootElement.GetProperty(name).GetString() ?? string.Empty;

    private static long ReadVersion(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("version").GetInt64();

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
