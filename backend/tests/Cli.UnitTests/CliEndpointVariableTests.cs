// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the order that decides which deployment a command sends its credential to.</summary>
/// <remarks>
/// Asserted against a stated value of the variable rather than against the process environment, which every other test
/// in this assembly shares and any of them could be reading while these run. That is the whole reason the variable
/// reaches the command through <see cref="CliContext.Variable" />: a developer who exported
/// <c>MAILFATHOM_ENDPOINT</c> for their own shell would otherwise have these tests resolve a deployment nobody asked
/// for, and the failure would be theirs alone to reproduce.
/// </remarks>
public sealed class CliEndpointVariableTests : IDisposable
{
    private const string SignedInProfileName = "production";

    private const string QuietDeployment = """
        {"synchronizationEnabled": false, "accounts": []}
        """;

    private static readonly Uri EndpointAddress = new("https://mail.example.test:8443");

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-endpoint-variable-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

    /// <summary>A shell that stated the deployment once for a session is what a command with no option acts on.</summary>
    [Fact]
    public async Task Command_AShellThatNamedADeployment_ActsOnWhatTheShellWasTold()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering(QuietDeployment);

        // Act: the name is one the store does not hold, so what the command resolved is in the refusal.
        var exitCode = await this.RunAsync(deployment, "staging", "mailbox", "status");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("staging", StringComparison.Ordinal));
    }

    /// <summary>What an operator typed beats what their shell was told, which is the order every other input here follows.</summary>
    [Fact]
    public async Task Command_AnEndpointOnTheCommandLine_BeatsWhatTheShellWasTold()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering(QuietDeployment);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "staging",
            "mailbox",
            "status",
            "--endpoint",
            SignedInProfileName);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.console.Failures);
    }

    /// <summary>A shell that said nothing leaves the profile the operator last switched to, which the store settles.</summary>
    [Fact]
    public async Task Command_AShellThatNamedNothing_ActsOnTheStoredDefault()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering(QuietDeployment);

        // Act
        var exitCode = await this.RunAsync(deployment, endpointVariable: null, "mailbox", "status");

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.console.Failures);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, string? endpointVariable, params string[] args)
    {
        var store = new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

        store.Save(SignedInProfileName, EndpointAddress, "not-a-real-key", "workstation");

        var context = new CliContext(
            this.console,
            store,
            (endpoint, trust) => FakeDeploymentTransport.Over(deployment, endpoint, trust),
            FakeMailboxRedirect.Silent(),
            _ => false,
            this.clock,
            Log: null,
            name => name == CliOptions.EndpointVariable ? endpointVariable : null);

        return CliRunner.RunAsync(context, args);
    }
}
