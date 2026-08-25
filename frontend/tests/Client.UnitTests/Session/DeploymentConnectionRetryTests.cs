// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Session;

namespace MailFathom.Client.UnitTests.Session;

/// <summary>The curve a client that has lost its deployment asks again on, and the bounds it never leaves.</summary>
public sealed class DeploymentConnectionRetryTests
{
    /// <summary>The first attempt is the one that found the connection gone, so nothing is waited before it.</summary>
    [Fact]
    public void WaitBefore_TheFirstAttempt_IsMadeWithoutWaiting()
    {
        // Arrange
        var retry = new DeploymentConnectionRetry(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

        // Act, Assert
        Assert.Equal(TimeSpan.Zero, retry.WaitBefore(1));
    }

    /// <summary>
    /// The wait doubles per retry and is drawn from the upper half of the grown range, so it always at least halves
    /// the rate of approach without ever running past the ceiling.
    /// </summary>
    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 4)]
    [InlineData(4, 4, 8)]
    [InlineData(5, 8, 16)]
    public void WaitBefore_ARetry_IsDrawnFromTheGrownRange(int attempt, int floorSeconds, int ceilingSeconds)
    {
        // Arrange
        var retry = new DeploymentConnectionRetry(8, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

        // Act
        var wait = retry.WaitBefore(attempt);

        // Assert
        Assert.InRange(wait, TimeSpan.FromSeconds(floorSeconds), TimeSpan.FromSeconds(ceilingSeconds));
    }

    /// <summary>
    /// The ceiling holds however many attempts are made, which is what keeps a client that stopped asking from being
    /// one that is still waiting out an arithmetic overflow.
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(int.MaxValue)]
    public void WaitBefore_AnyAttempt_NeverRunsPastTheCeiling(int attempt)
    {
        // Arrange
        var retry = new DeploymentConnectionRetry(int.MaxValue, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

        // Act
        var wait = retry.WaitBefore(attempt);

        // Assert
        Assert.InRange(wait, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    /// <summary>A policy stating no wait at all waits none, which is what a test asserting the attempts is handed.</summary>
    [Fact]
    public void WaitBefore_APolicyStatingNoWait_WaitsNone()
    {
        // Arrange
        var retry = new DeploymentConnectionRetry(3, TimeSpan.Zero, TimeSpan.Zero);

        // Act, Assert
        Assert.Equal(TimeSpan.Zero, retry.WaitBefore(2));
        Assert.Equal(TimeSpan.Zero, retry.WaitBefore(3));
    }

    /// <summary>What the application registers is bounded and short enough to be told about rather than waited out.</summary>
    [Fact]
    public void Standard_WhatTheApplicationRegisters_IsBoundedAndStatesItsOwnCeiling()
    {
        // Act
        var retry = DeploymentConnectionRetry.Standard;

        // Assert
        Assert.Equal(5, retry.Attempts);
        Assert.InRange(retry.FirstWait, TimeSpan.FromSeconds(1), retry.LongestWait);
        Assert.InRange(retry.LongestWait, retry.FirstWait, TimeSpan.FromMinutes(1));
    }

    /// <summary>A policy that could state no attempt, a negative wait, or a ceiling below its own floor is not one.</summary>
    [Fact]
    public void Constructor_APolicyThatContradictsItself_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DeploymentConnectionRetry(0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DeploymentConnectionRetry(5, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(30)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DeploymentConnectionRetry(5, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }

    /// <summary>An attempt counted from anything but one is a caller that has lost count rather than a curve to draw.</summary>
    [Fact]
    public void WaitBefore_AnAttemptThatIsNotOne_IsRefused()
    {
        // Arrange
        var retry = DeploymentConnectionRetry.Standard;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => retry.WaitBefore(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => retry.WaitBefore(-1));
    }
}
