// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the commands that provision, list, rotate, suspend, and remove an owner's sign-in.</summary>
/// <remarks>
/// The claim these exist for is the one no other suite can make: a password reaches the request and nothing else. It is
/// never an argument, so nothing about the invocation could carry it into a shell history or a process table; it is
/// never printed, so no confirmation or refusal repeats it; and the prompt is what the command asks with whether
/// somebody is at the terminal or a script is piping one line in.
/// </remarks>
public sealed class OwnerCredentialCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private const string Password = "correcthorsebatterystaple";

    private static readonly Guid Owner = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnotherOwner = new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid CredentialId = new("33333333-3333-3333-3333-333333333333");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The password is typed at a prompt and sent in the body, which is the whole reason no option carries one.</summary>
    [Fact]
    public async Task Create_APasswordTypedAtThePrompt_SendsItInTheBodyAndNowhereElse()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var provisioning = Assert.Single(deployment.RequestsTo(
            HttpMethod.Post,
            AdminEndpointRoutes.OwnerCredentialsPath(Owner)));

        Assert.Equal(Password, ReadField(provisioning.ContentAsUtf8String(), "password"));
        Assert.Equal("owner", ReadField(provisioning.ContentAsUtf8String(), "username"));
    }

    /// <summary>Everything a person reads is written down somewhere, so the one thing that was typed is in none of it.</summary>
    [Fact]
    public async Task Create_AProvisionedCredential_ReportsTheIdentifierWithoutRepeatingThePassword()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.Contains(Password, StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Errors, line => line.Contains(Password, StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"{FakeOwnerCredentialDeployment.ProvisionedCredentialId:D}", StringComparison.Ordinal));
    }

    /// <summary>Reading it without echo is what keeps it off the screen, and asking for it is what keeps it off the command line.</summary>
    [Fact]
    public async Task Create_Always_AsksForThePasswordRatherThanAcceptingOneAsAnArgument()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.NotNull(this.harness.Console.LastPrompt);
        Assert.Contains("owner", this.harness.Console.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, this.harness.Console.LastPrompt, StringComparison.Ordinal);
    }

    /// <summary>An exhausted pipe supplies nothing, which is not a decision to provision a credential with an empty password.</summary>
    [Fact]
    public async Task Create_NoPasswordSupplied_SendsNothingAndSaysWhatToDo()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);
        this.harness.Console.SecretToSupply = string.Empty;

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerCredentialsPath(Owner)));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("No password was supplied", StringComparison.Ordinal));
    }

    /// <summary>The deployment states the policy, so its refusal is repeated rather than restated — and it names the rule rather than the value.</summary>
    [Fact]
    public async Task Create_APasswordTheDeploymentRefuses_RepeatsItsReasonWithoutThePassword()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Refusing(
            [Owner],
            HttpStatusCode.BadRequest,
            "A password is at least 12 characters long.");
        this.harness.Console.SecretToSupply = "short";

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("at least 12 characters", StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Errors, line => line.Contains("short", StringComparison.Ordinal));
    }

    /// <summary>A username names one credential across the deployment, and the operator is told that rather than told nothing happened.</summary>
    [Fact]
    public async Task Create_AUsernameAlreadyHeld_RepeatsWhatTheDeploymentSaid()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Refusing(
            [Owner],
            HttpStatusCode.Conflict,
            "Another credential already signs in as 'owner'.");
        this.harness.Console.SecretToSupply = Password;

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("already signs in", StringComparison.Ordinal));
    }

    /// <summary>The deployment this serves holds one owner, so the ordinary invocation names none and acts on the one there is.</summary>
    [Fact]
    public async Task Create_ADeploymentHoldingOneOwner_ActsForThatOwnerWithoutBeingTold()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerCredentialsPath(Owner)));
    }

    /// <summary>Guessing which of several owners a credential is for would provision a way into the wrong person's mail.</summary>
    [Fact]
    public async Task Create_ADeploymentHoldingSeveralOwners_RefusesAndNamesTheIdentifiersToChooseFrom()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner, AnotherOwner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "create", "--username", "owner", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerCredentialsPath(Owner)));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("--owner", StringComparison.Ordinal)
                && line.Contains($"{AnotherOwner:D}", StringComparison.Ordinal));
    }

    /// <summary>A named owner is acted on as written, because the deployment refuses one it holds no record for and says so.</summary>
    [Fact]
    public async Task Create_AnOwnerTheInvocationNamed_ActsForThatOwnerWithoutReadingTheRoster()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner, AnotherOwner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            "create",
            "--username",
            "owner",
            "--owner",
            $"{AnotherOwner:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.OwnerCredentialsPath(AnotherOwner)));
        Assert.Empty(deployment.RequestsTo(HttpMethod.Get, AdminEndpointRoutes.OwnersPath));
    }

    /// <summary>A listing is a fact about the record rather than about the secret, which is what makes it safe to print and keep.</summary>
    [Fact]
    public async Task List_AnOwnerHoldingCredentials_ReportsEachOneWithNothingDerivedFromItsPassword()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding(
            [Owner],
            FakeOwnerCredentialDeployment.Credential(CredentialId, "owner"),
            FakeOwnerCredentialDeployment.Credential(Guid.Empty, "owner.reader", enabled: false));

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("owner.reader", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("disabled", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("enabled", StringComparison.Ordinal));
    }

    /// <summary>Nothing provisions a credential on its own, so an empty listing is a state to explain rather than a table with no rows.</summary>
    [Fact]
    public async Task List_AnOwnerHoldingNone_SaysSoRatherThanDrawingAnEmptyTable()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("credential create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rotate_ANewPasswordTypedAtThePrompt_SendsItToTheCredentialsOwnPasswordRoute()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);
        this.harness.Console.SecretToSupply = Password;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            "rotate",
            "--id",
            $"{CredentialId:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var rotation = Assert.Single(deployment.RequestsTo(
            HttpMethod.Put,
            AdminEndpointRoutes.OwnerCredentialPasswordPath(Owner, CredentialId)));

        Assert.Equal(Password, ReadField(rotation.ContentAsUtf8String(), "password"));
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.Contains(Password, StringComparison.Ordinal));
    }

    /// <summary>Turning a way into somebody's mail on and turning it off are opposite decisions, so each is its own command.</summary>
    [Theory]
    [InlineData("enable", true)]
    [InlineData("disable", false)]
    public async Task SetEnabled_EitherCommand_SendsTheDecisionItsNameStates(string verb, bool written)
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            verb,
            "--id",
            $"{CredentialId:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var decision = Assert.Single(deployment.RequestsTo(
            HttpMethod.Put,
            AdminEndpointRoutes.OwnerCredentialEnablementPath(Owner, CredentialId)));

        Assert.Equal(written, ReadFlag(decision.ContentAsUtf8String(), "enabled"));
    }

    /// <summary>Removal cannot be undone, so the credential is shown and then agreed to rather than removed on sight.</summary>
    [Fact]
    public async Task Delete_AnAgreedRemoval_ShowsTheCredentialBeforeRemovingIt()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding(
            [Owner],
            FakeOwnerCredentialDeployment.Credential(CredentialId, "owner"));
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            "delete",
            "--id",
            $"{CredentialId:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(this.harness.Console.Questions);
        Assert.Single(deployment.RequestsTo(
            HttpMethod.Delete,
            AdminEndpointRoutes.OwnerCredentialPath(Owner, CredentialId)));
    }

    [Fact]
    public async Task Delete_AnOperatorWhoDeclines_RemovesNothing()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding(
            [Owner],
            FakeOwnerCredentialDeployment.Credential(CredentialId, "owner"));
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            "delete",
            "--id",
            $"{CredentialId:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(
            HttpMethod.Delete,
            AdminEndpointRoutes.OwnerCredentialPath(Owner, CredentialId)));
    }

    /// <summary>A redirected input has nobody to ask, and reading an agreement out of whatever was piped in would remove a record nothing can put back.</summary>
    [Fact]
    public async Task Delete_NobodyAtTheTerminal_RefusesRatherThanGuessing()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding(
            [Owner],
            FakeOwnerCredentialDeployment.Credential(CredentialId, "owner"));
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            "delete",
            "--id",
            $"{CredentialId:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(
            HttpMethod.Delete,
            AdminEndpointRoutes.OwnerCredentialPath(Owner, CredentialId)));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>An identifier out of the wrong listing removes nothing, and the command says so before anything is agreed to.</summary>
    [Fact]
    public async Task Delete_ACredentialTheOwnerDoesNotHold_AsksNothingAndRemovesNothing()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "credential",
            "delete",
            "--id",
            $"{CredentialId:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.harness.Console.Questions);
        Assert.Empty(deployment.RequestsTo(
            HttpMethod.Delete,
            AdminEndpointRoutes.OwnerCredentialPath(Owner, CredentialId)));
    }

    /// <summary>Nothing about an invocation may carry the password, which is what keeps it out of a shell history and a process table.</summary>
    [Fact]
    public async Task EveryCommand_TheHelpItPublishes_OffersNoOptionCarryingAPassword()
    {
        // Arrange
        using var deployment = FakeOwnerCredentialDeployment.Holding([Owner]);

        // Act
        var exitCode = await this.RunAsync(deployment, "credential", "create", "--help");

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.DoesNotContain(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("--password", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => this.harness.Dispose();

    private static string ReadField(string body, string name) =>
        JsonDocument.Parse(body).RootElement.GetProperty(name).GetString() ?? string.Empty;

    private static bool ReadFlag(string body, string name) =>
        JsonDocument.Parse(body).RootElement.GetProperty(name).GetBoolean();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
