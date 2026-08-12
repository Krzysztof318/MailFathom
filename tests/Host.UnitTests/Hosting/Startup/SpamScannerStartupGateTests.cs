// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Scanning;
using MailFathom.Host.Hosting.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers the refusal that keeps a deployment from classifying mail without the scanner it was told to consult.</summary>
public sealed class SpamScannerStartupGateTests
{
    [Fact]
    public async Task StartAsync_ADaemonThatAnswers_ReportsTheScannerGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.SpamScanner);
        var probe = Substitute.For<ISpamScannerProbe>();

        // Act
        await CreateGate(probe, startupGates).StartAsync(CancellationToken.None);

        // Assert
        Assert.True(startupGates.Completed);
        await probe.Received(1).VerifyAvailableAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The scanner is the guarded dependency that fails open on one message, which is exactly why an absent one has to
    /// stop the process: every message would keep the verdict its headers reached while the configuration said a corpus
    /// had read it, and nothing would look wrong.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADaemonThatCannotBeReached_FailsStartupAndLeavesTheGateOutstanding()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.SpamScanner);
        var probe = Substitute.For<ISpamScannerProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(SpamScannerUnavailableException.NotReached(
                "mailfathom-spamassassin:783",
                new InvalidOperationException("connection refused")));

        // Act
        var failure = await Assert.ThrowsAsync<SpamScannerUnavailableException>(
            () => CreateGate(probe, startupGates).StartAsync(CancellationToken.None));

        // Assert
        Assert.False(startupGates.Completed);
        Assert.Equal("mailfathom-spamassassin:783", failure.Endpoint);
    }

    /// <summary>Something listening on the port that is not a spam daemon fails startup the same way nothing does.</summary>
    [Fact]
    public async Task StartAsync_SomethingThatIsNotASpamDaemon_FailsStartupAsWell()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.SpamScanner);
        var probe = Substitute.For<ISpamScannerProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(SpamScannerUnavailableException.NotASpamDaemon("mailfathom-spamassassin:783"));

        // Act, Assert
        _ = await Assert.ThrowsAsync<SpamScannerUnavailableException>(
            () => CreateGate(probe, startupGates).StartAsync(CancellationToken.None));
        Assert.False(startupGates.Completed);
    }

    [Fact]
    public async Task StopAsync_Always_DoesNothing()
    {
        // Arrange
        var gate = CreateGate(Substitute.For<ISpamScannerProbe>(), new HostStartupGates());

        // Act, Assert
        await gate.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SpamScannerStartupGate(
            null!,
            new HostStartupGates(),
            NullLogger<SpamScannerStartupGate>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SpamScannerStartupGate(
            Substitute.For<ISpamScannerProbe>(),
            null!,
            NullLogger<SpamScannerStartupGate>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SpamScannerStartupGate(
            Substitute.For<ISpamScannerProbe>(),
            new HostStartupGates(),
            null!));
    }

    private static SpamScannerStartupGate CreateGate(ISpamScannerProbe probe, HostStartupGates startupGates) =>
        new(probe, startupGates, NullLogger<SpamScannerStartupGate>.Instance);
}
