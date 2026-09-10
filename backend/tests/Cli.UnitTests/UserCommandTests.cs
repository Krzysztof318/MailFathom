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
/// Covers the commands that record a user, list them, maintain their mailboxes, adopt them out of configuration, and
/// erase them. What these hold is the part of each act that lives in the command rather than in the deployment: the
/// version a write is composed over, the confirmation the two destructive acts ask for, and what a refusal tells an
/// operator to do next.
/// </summary>
public sealed class UserCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    /// <summary>The code a deployment refuses a write to a configuration-served user with.</summary>
    private const int RecordReadFromConfiguration = 12015;

    /// <summary>The code a deployment refuses a record composed over a version another writer moved past with.</summary>
    private const int VersionSuperseded = 12008;

    /// <summary>A record declaring one mailbox, which an editing session opens over.</summary>
    private const string OneMailAccount = """{"MailAccounts":[{"AccountId":"work"}]}""";

    /// <summary>A record whose secret-bearing value the deployment replaced with its redaction marker.</summary>
    private const string RedactedMailAccount =
        """{"MailAccounts":[{"AccountId":"work","Password":{"Secret":"(redacted)"}}]}""";

    private static readonly Guid User = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnotherUser = new("22222222-2222-2222-2222-222222222222");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Where a declaration this suite hands to <c>--from-file</c> is written, cleaned up with the suite.</summary>
    private readonly string declarations =
        Path.Combine(Path.GetTempPath(), $"mailfathom-user-tests-{Guid.NewGuid():N}");

    /// <summary>The identifier is the deployment's to mint, so what comes back is the one thing a script cannot reconstruct from what it typed.</summary>
    [Fact]
    public async Task Add_ALabelTheDeploymentAccepts_ReportsTheIdentifierItMinted()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingNobody();

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "add", "--display-name", "alex", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var recording = Assert.Single(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UsersPath));

        Assert.Equal("alex", ReadField(recording.ContentAsUtf8String(), "displayName"));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("55555555", StringComparison.Ordinal));
    }

    /// <summary>A new user's mailboxes are their own record's from the first moment, so the operator is told where to declare one.</summary>
    [Fact]
    public async Task Add_ARecordedUser_SaysWhereTheirMailAccountsAreDeclared()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingNobody();

        // Act
        await this.RunAsync(deployment, "user", "add", "--display-name", "alex", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("mfctl user account add", StringComparison.Ordinal));
    }

    /// <summary>A label is the operator's own text and nothing is keyed by it, so a rename asks nothing and reports what the user now carries.</summary>
    [Fact]
    public async Task Rename_AUserTheDeploymentHolds_SendsTheLabelAndReportsIt()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "rename",
            "--user",
            $"{User:D}",
            "--display-name",
            "alexandra",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var rename = Assert.Single(
            deployment.UserRequestsTo(HttpMethod.Put, AdminEndpointRoutes.UserDisplayNamePath(User)));

        Assert.Equal("alexandra", ReadField(rename.ContentAsUtf8String(), "displayName"));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("alexandra", StringComparison.Ordinal));
    }

    /// <summary>
    /// A start reads a declared user's label from the file it is declared in, so a rename of one lasts until the next
    /// restart. Reporting the new label without saying so would report a change the deployment undoes.
    /// </summary>
    [Fact]
    public async Task Rename_AUserAConfigurationSourceDeclares_SaysTheLabelLastsUntilARestart()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "rename",
            "--user",
            $"{User:D}",
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

    /// <summary>A user nothing declares keeps the label a rename writes, so nothing qualifies what the command reported.</summary>
    [Fact]
    public async Task Rename_AUserNoConfigurationSourceDeclares_ReportsTheLabelWithNothingQualifyingIt()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        await this.RunAsync(
            deployment,
            "user",
            "rename",
            "--user",
            $"{User:D}",
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
    public async Task Rename_NoUserNamedOnADeploymentHoldingOne_ActsForTheSingleUserItHolds()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "rename",
            "--display-name",
            "alexandra",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.UserRequestsTo(HttpMethod.Put, AdminEndpointRoutes.UserDisplayNamePath(User)));
    }

    /// <summary>The listing is where the two states that decide what to do next are read.</summary>
    [Fact]
    public async Task List_AUserServedFromConfiguration_SaysTheAdoptionIsWhatMovesThem()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("mfctl user adopt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_AUserReadingTheirOwnRecord_SaysWhereTheirMailAccountsAreMaintained()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        await this.RunAsync(deployment, "user", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("their own record", StringComparison.Ordinal));
    }

    /// <summary>An empty roster is a deployment that has not started successfully, which is worth saying rather than printing nothing.</summary>
    [Fact]
    public async Task List_ADeploymentHoldingNoUser_SaysWhatAnEmptyRosterMeans()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingNobody();

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("has not started successfully", StringComparison.Ordinal));
    }

    /// <summary>A deployment serving one user needs no identifier typed, which is what makes the ordinary invocation short.</summary>
    [Fact]
    public async Task Show_ADeploymentHoldingOneUser_ResolvesThemWithoutAnIdentifierBeingTyped()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "show", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.UserRequestsTo(HttpMethod.Get, AdminEndpointRoutes.UserRecordPath(User)));
    }

    /// <summary>Composing a caller against whichever user came first is how one person is handed another's mail, so the command refuses to guess.</summary>
    [Fact]
    public async Task Show_ADeploymentHoldingSeveralUsers_RefusesToGuessWhichOneWasMeant()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User, AnotherUser);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "show", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Get, AdminEndpointRoutes.UserRecordPath(User)));
    }

    /// <summary>A record whose mailboxes are in a file is empty, and reading that without being told why looks like a user with no mailboxes.</summary>
    [Fact]
    public async Task Show_AUserServedFromConfiguration_SaysWhyTheRecordIsEmpty()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User);

        // Act
        await this.RunAsync(deployment, "user", "show", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("mfctl user adopt", StringComparison.Ordinal));
    }

    /// <summary>
    /// The record is read first so the write is composed over the version it was read at, which is what makes two
    /// administrators declaring a mailbox at once produce a refusal rather than one silently dropping the other's.
    /// </summary>
    [Fact]
    public async Task AccountAdd_ADeclarationInAFile_ComposesTheWriteOverTheVersionItRead()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);
        var declaration = await this.WriteDeclarationAsync("""{"AccountId":"archive"}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "account",
            "add",
            "--from-file",
            declaration,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var written = Assert.Single(
            deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserMailAccountsPath(User)));

        Assert.Equal(FakeUserRecordDeployment.RecordVersion, ReadVersion(written.ContentAsUtf8String()));
        Assert.Contains("archive", ReadField(written.ContentAsUtf8String(), "account"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountAdd_APathNothingIsAt_SaysSoWithoutReachingTheDeployment()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "account",
            "add",
            "--from-file",
            Path.Combine(Path.GetTempPath(), $"mailfathom-absent-{Guid.NewGuid():N}.json"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserMailAccountsPath(User)));
    }

    [Fact]
    public async Task AccountAdd_AnEmptyFile_SaysItDeclaresNoMailAccount()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);
        var declaration = await this.WriteDeclarationAsync("   ");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
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

    /// <summary>The one refusal a command can repair names the repair, which is the adoption that moves the user out of the files.</summary>
    [Fact]
    public async Task AccountAdd_AUserAConfigurationSourceSupplies_NamesTheAdoptionAsTheRepair()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.RefusingTheWrite(
            User,
            RecordReadFromConfiguration,
            "This user's mail accounts are supplied by a configuration source.");

        var declaration = await this.WriteDeclarationAsync("""{"AccountId":"archive"}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
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
            line => line.Contains("mfctl user adopt", StringComparison.Ordinal));
    }

    /// <summary>No configuration change takes somebody's mail away, so a withdrawal says what it did not do.</summary>
    [Fact]
    public async Task AccountRemove_AnIdentifierTheRecordDeclares_SaysTheStoredMailWasNotTouched()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
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

    /// <summary>A user already reading their own record has nothing to move, and saying so is not a refusal.</summary>
    [Fact]
    public async Task Adopt_AUserAlreadyReadingTheirOwnRecord_SaysThereIsNothingToAdopt()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "adopt", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserAdoptionPath(User)));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("nothing to adopt", StringComparison.Ordinal));
    }

    /// <summary>The preview names the mailboxes and the path behind them, which is the moment to notice it covers more than was meant.</summary>
    [Fact]
    public async Task Adopt_AUserAConfigurationSourceSupplies_PreviewsTheMailboxesAndThePathBehindThem()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User, "primary", "archive");

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "adopt", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("primary", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("MailSynchronization:Accounts", StringComparison.Ordinal));
    }

    /// <summary>The adoption is composed over the version the preview reported, which is what the deployment accepts it against.</summary>
    [Fact]
    public async Task Adopt_AUserAConfigurationSourceSupplies_ComposesTheAdoptionOverThePreviewedVersion()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User, "primary");

        // Act
        await this.RunAsync(deployment, "user", "adopt", "--yes", "--endpoint", Endpoint);

        // Assert
        var adoption = Assert.Single(
            deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserAdoptionPath(User)));

        Assert.Equal(FakeUserRecordDeployment.RecordVersion, ReadVersion(adoption.ContentAsUtf8String()));
    }

    /// <summary>Nobody is at the terminal, so an unconfirmed adoption is refused with the flag that states the agreement in the command.</summary>
    [Fact]
    public async Task Adopt_NoAgreementStatedAndNobodyAtTheTerminal_AdoptsNothing()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User, "primary");

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "adopt", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserAdoptionPath(User)));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("Nothing was adopted", StringComparison.Ordinal));
    }

    /// <summary>An identifier copied out of the wrong listing looks the same either way, so the confirmation names the person.</summary>
    [Fact]
    public async Task Remove_AConfirmedErasure_NamesThePersonRatherThanOnlyTheIdentifier()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "remove",
            "--user",
            $"{User:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains($"user-{User:D}", StringComparison.Ordinal));
    }

    /// <summary>Nothing here undoes an erasure, so it is never performed on an exhausted pipe.</summary>
    [Fact]
    public async Task Remove_NoAgreementStatedAndNobodyAtTheTerminal_ErasesNothing()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "remove",
            "--user",
            $"{User:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Delete, AdminEndpointRoutes.UserPath(User)));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("Nothing was erased", StringComparison.Ordinal));
    }

    /// <summary>A process serving an erased user follows the erasure without asking for a restart.</summary>
    [Fact]
    public async Task Remove_AnErasedUserTheProcessWasServing_SaysNoRestartIsNeeded()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        await this.RunAsync(deployment, "user", "remove", "--user", $"{User:D}", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.DoesNotContain(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("Restart it", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("has stopped serving this user", StringComparison.Ordinal));
    }

    /// <summary>A user the deployment does not hold is nothing to erase rather than a failure, and the confirmation is never reached.</summary>
    [Fact]
    public async Task Remove_AUserTheDeploymentDoesNotHold_SaysThereIsNothingToErase()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.Holding(User);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "remove",
            "--user",
            $"{AnotherUser:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Delete, AdminEndpointRoutes.UserPath(AnotherUser)));
    }

    /// <summary>What the operator saved is what is committed, over the version the buffer was opened at.</summary>
    [Fact]
    public async Task Edit_AnEditedRecord_CommitsWhatWasSavedOverTheVersionItWasOpenedAt()
    {
        // Arrange
        const string edited = """{"MailAccounts":[{"AccountId":"work"},{"AccountId":"family"}]}""";

        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, OneMailAccount);

        this.harness.EditsTheBufferInto(edited);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = Assert.Single(
            deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));

        Assert.Equal(FakeUserRecordDeployment.RecordVersion, ReadVersion(sent.ContentAsUtf8String()));
        Assert.Equal(edited, ReadField(sent.ContentAsUtf8String(), "document"));
    }

    /// <summary>
    /// The buffer is the record the deployment answered with, byte for byte. What a record may carry is the deployment's
    /// decision, so what is asserted here is that this command neither composes a document of its own nor rewrites the
    /// one it was given.
    /// </summary>
    [Fact]
    public async Task Edit_ARecordTheDeploymentRedacted_OpensTheBufferOnWhatTheDeploymentAnswered()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, RedactedMailAccount);

        var opened = string.Empty;
        this.harness.OpensTheBufferWith((_, path) =>
        {
            opened = File.ReadAllText(path);

            return true;
        });

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(RedactedMailAccount, opened);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));
    }

    /// <summary>An emptied buffer is how every editor-driven command an operator has met is abandoned, and it is honoured as one.</summary>
    [Fact]
    public async Task Edit_AnEmptiedBuffer_LeavesTheRecordAsItWas()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, OneMailAccount);

        this.harness.EditsTheBufferInto(string.Empty);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("emptied", StringComparison.Ordinal));
    }

    /// <summary>A record saved back as it was opened is nothing to commit, and spending a version on it would be a change the operator did not ask for.</summary>
    [Fact]
    public async Task Edit_ABufferSavedUnchanged_WritesNothing()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, OneMailAccount);

        this.harness.EditsTheBufferInto(OneMailAccount);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));
    }

    /// <summary>Nothing is opened without an editor to open it in, and nothing about one person's mailboxes is fetched for a session that cannot start.</summary>
    [Fact]
    public async Task Edit_NoEditorNamedByTheShell_FailsWithoutReadingTheRecord()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, OneMailAccount);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains(OperatorEditor.VisualVariable, StringComparison.Ordinal));
        Assert.Empty(deployment.RecordedRequests);
    }

    /// <summary>An editor that did not finish is a session that produced nothing, so nothing is read back and nothing is sent.</summary>
    [Fact]
    public async Task Edit_AnEditorThatDidNotFinish_FailsWithoutWritingAnything()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, OneMailAccount);

        this.harness.OpensTheBufferWith((_, _) => false);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));
    }

    /// <summary>The deployment this serves usually holds one person, so the ordinary invocation names nobody.</summary>
    [Fact]
    public async Task Edit_ADeploymentHoldingOneUser_ActsOnThatUserWithoutBeingToldWhich()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.HoldingRecords(User, OneMailAccount);

        this.harness.EditsTheBufferInto("""{"MailAccounts":[]}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));
    }

    /// <summary>
    /// A record somebody else committed over is refused rather than merged, and what an operator has to see is what that
    /// writer changed. The other writer is routinely the person themselves from the client.
    /// </summary>
    [Fact]
    public async Task Edit_AVersionAnotherWriterMovedPast_ReportsTheSettingsThatMoved()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.RefusingTheWrite(
            User,
            VersionSuperseded,
            "The record was composed over version 3 and version 4 is in force.",
            OneMailAccount,
            """{"MailAccounts":[{"AccountId":"work"},{"AccountId":"family"}]}""");

        this.harness.EditsTheBufferInto("""{"MailAccounts":[{"AccountId":"archive"}]}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("version 4 is in force", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("MailAccounts:1:AccountId", StringComparison.Ordinal));
    }

    /// <summary>A record a file still supplies is refused before the editor opens, because the write was never going to be accepted and the operator's session would be spent on it.</summary>
    [Fact]
    public async Task Edit_ARecordAConfigurationSourceSupplies_RefusesWithoutOpeningTheEditor()
    {
        // Arrange
        using var deployment = FakeUserRecordDeployment.SupplyingFromConfiguration(User, "work");

        var openedTheEditor = false;
        this.harness.OpensTheBufferWith((_, _) =>
        {
            openedTheEditor = true;

            return true;
        });

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "edit", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.False(openedTheEditor);
        Assert.Empty(deployment.UserRequestsTo(HttpMethod.Post, AdminEndpointRoutes.UserRecordPath(User)));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("mfctl user adopt", StringComparison.Ordinal));
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
