// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting.Startup;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers the holder every admitted caller on a mail-reading surface is composed against.</summary>
public sealed class DeploymentMailOwnerTests
{
    [Fact]
    public void Owner_AfterTheGateResolvedIt_ReportsTheOwnerTheDeploymentServes()
    {
        // Arrange
        var deploymentOwner = new DeploymentMailOwner();

        // Act
        deploymentOwner.Resolved(SyntheticMailOwner.Deployment);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, deploymentOwner.Owner);
    }

    /// <summary>
    /// Reading it before the gate has settled it is a wiring defect rather than a deployment's problem, so it fails as
    /// one instead of answering with the identity that names nobody — which every unresolved holder would agree on.
    /// </summary>
    [Fact]
    public void Owner_BeforeTheGateResolvedIt_FailsRatherThanNamingNobody()
    {
        // Arrange
        var deploymentOwner = new DeploymentMailOwner();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => deploymentOwner.Owner);
    }

    /// <summary>The struct default is what an unread row and an unassigned field both look like, and neither is an owner.</summary>
    [Fact]
    public void Resolved_AnOwnerThatNamesNobody_IsRejected()
    {
        // Arrange
        var deploymentOwner = new DeploymentMailOwner();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => deploymentOwner.Resolved(default));
    }
}
