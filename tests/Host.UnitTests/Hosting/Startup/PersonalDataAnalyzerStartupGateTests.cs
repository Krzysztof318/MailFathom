// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Host.Hosting.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers the refusal that keeps a deployment with no analyzer from serving a protection that is not in force.</summary>
public sealed class PersonalDataAnalyzerStartupGateTests
{
    [Fact]
    public async Task StartAsync_AnalyzerThatAnswers_ReportsTheAnalyzerGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.PersonalDataAnalyzer);
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();

        // Act
        await CreateGate(probe, startupGates).StartAsync(CancellationToken.None);

        // Assert
        Assert.True(startupGates.Completed);
        await probe.Received(1).VerifyAvailableAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The scanner fails closed, so an instance that logged this and served anyway would look healthy while refusing every
    /// read, derived write, and egress it guards.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnalyzerThatDoesNotAnswer_FailsStartupAndLeavesTheGateOutstanding()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.PersonalDataAnalyzer);
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(PersonalDataAnalyzerUnavailableException.NotReached(
                "http://presidio-analyzer:3000/",
                new HttpRequestException("connection refused")));

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => CreateGate(probe, startupGates).StartAsync(CancellationToken.None));

        // Assert
        Assert.False(startupGates.Completed);
        Assert.Equal("http://presidio-analyzer:3000/", failure.Endpoint);
    }

    [Fact]
    public async Task StopAsync_Always_DoesNothing()
    {
        // Arrange
        var gate = CreateGate(Substitute.For<IPersonalDataAnalyzerProbe>(), new HostStartupGates());

        // Act, Assert
        await gate.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new PersonalDataAnalyzerStartupGate(
            null!,
            new HostStartupGates(),
            NullLogger<PersonalDataAnalyzerStartupGate>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PersonalDataAnalyzerStartupGate(
            Substitute.For<IPersonalDataAnalyzerProbe>(),
            null!,
            NullLogger<PersonalDataAnalyzerStartupGate>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PersonalDataAnalyzerStartupGate(
            Substitute.For<IPersonalDataAnalyzerProbe>(),
            new HostStartupGates(),
            null!));
    }

    private static PersonalDataAnalyzerStartupGate CreateGate(
        IPersonalDataAnalyzerProbe probe,
        HostStartupGates startupGates) =>
        new(probe, startupGates, NullLogger<PersonalDataAnalyzerStartupGate>.Instance);
}
