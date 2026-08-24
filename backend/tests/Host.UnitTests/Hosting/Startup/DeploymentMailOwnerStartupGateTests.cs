// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Hosting.Startup;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>
/// Covers the single-owner invariant a deployment whose accounts live in configuration is served under: a configured
/// account names no owner, so exactly one owner record is what makes the question answerable at all.
/// </summary>
public sealed class DeploymentMailOwnerStartupGateTests
{
    [Fact]
    public async Task StartAsync_OneOwnerRecord_PublishesItAsTheOwnerEveryConfiguredAccountBelongsTo()
    {
        // Arrange
        var deploymentOwner = new DeploymentMailOwner();

        // Act
        await CreateGate([SyntheticMailOwner.Deployment], deploymentOwner).StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, deploymentOwner.Owner);
    }

    [Fact]
    public async Task StartAsync_OneOwnerRecord_ReportsTheOwnerGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.DeploymentMailOwner);

        // Act
        await CreateGate([SyntheticMailOwner.Deployment], startupGates: startupGates)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>
    /// No owner is the schema not having been applied, and several is a deployment that has acquired owner records
    /// while its accounts are still declared in a file that cannot say whose they are. Serving either would mean
    /// attributing every configured account to whichever owner a query returned first.
    /// </summary>
    [Theory]
    [MemberData(nameof(OwnerRecordsThatCannotBeServed))]
    public async Task StartAsync_OtherThanExactlyOneOwnerRecord_FailsStartup(MailOwnerId[] owners)
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(owners).StartAsync(CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.DeploymentMailOwnerUnresolved, refusal.ErrorCode);
    }

    /// <summary>A gate that failed took the host down with it, so nothing may report the host as having come up.</summary>
    [Theory]
    [MemberData(nameof(OwnerRecordsThatCannotBeServed))]
    public async Task StartAsync_OtherThanExactlyOneOwnerRecord_LeavesTheOwnerGateOutstanding(MailOwnerId[] owners)
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.DeploymentMailOwner);

        // Act
        await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(owners, startupGates: startupGates).StartAsync(CancellationToken.None));

        // Assert
        Assert.False(startupGates.Completed);
    }

    /// <summary>
    /// Reading one row more than a deployment may hold is what makes "several" observable at all; reading exactly one
    /// would report every deployment as holding one.
    /// </summary>
    [Fact]
    public async Task StartAsync_AlwaysGiven_ReadsOneOwnerMoreThanADeploymentMayHold()
    {
        // Arrange
        var owners = Substitute.For<IMailOwnerDirectory>();
        owners.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailOwnerId>>([SyntheticMailOwner.Deployment]));

        // Act
        await CreateGate(owners).StartAsync(CancellationToken.None);

        // Assert
        await owners.Received(1).ReadOwnersAsync(2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_CallerCancelled_PropagatesTheTokenToTheDirectory()
    {
        // Arrange
        var owners = Substitute.For<IMailOwnerDirectory>();
        owners.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailOwnerId>>([SyntheticMailOwner.Deployment]));
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(owners).StartAsync(cancellation.Token);

        // Assert
        await owners.Received(1).ReadOwnersAsync(Arg.Any<int>(), cancellation.Token);
    }

    public static TheoryData<MailOwnerId[]> OwnerRecordsThatCannotBeServed() => new()
    {
        Array.Empty<MailOwnerId>(),
        new[] { SyntheticMailOwner.Deployment, SyntheticMailOwner.Another },
    };

    private static DeploymentMailOwnerStartupGate CreateGate(
        IReadOnlyList<MailOwnerId> owners,
        DeploymentMailOwner? deploymentOwner = null,
        HostStartupGates? startupGates = null)
    {
        var directory = Substitute.For<IMailOwnerDirectory>();
        directory.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(owners));

        return CreateGate(directory, deploymentOwner, startupGates);
    }

    private static DeploymentMailOwnerStartupGate CreateGate(
        IMailOwnerDirectory owners,
        DeploymentMailOwner? deploymentOwner = null,
        HostStartupGates? startupGates = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => owners);

        return new DeploymentMailOwnerStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            deploymentOwner ?? new DeploymentMailOwner(),
            startupGates ?? new HostStartupGates(HostStartupGate.DeploymentMailOwner),
            NullLogger<DeploymentMailOwnerStartupGate>.Instance);
    }
}
