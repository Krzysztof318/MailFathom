// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Collection;

/// <summary>Covers the bound one synchronization run records contacts under.</summary>
public sealed class ContactCollectionBudgetTests
{
    /// <summary>A run may record up to its ceiling and no further, so a first pass over years of mail is paced.</summary>
    [Fact]
    public void TryClaim_AskedPastTheCeiling_AdmitsExactlyTheCeilingAndCountsWhatItAdmitted()
    {
        // Arrange
        var budget = new ContactCollectionBudget(3);

        // Act
        var admitted = Enumerable.Range(0, 6).Select(_ => budget.TryClaim()).ToArray();

        // Assert
        Assert.Equal([true, true, true, false, false, false], admitted);
        Assert.Equal(3, budget.Recorded);
    }

    /// <summary>An operator asking for nothing to be collected gets a run that records nobody rather than one contact.</summary>
    [Fact]
    public void TryClaim_ABudgetOfNothing_RefusesTheFirstClaim()
    {
        // Arrange
        var budget = new ContactCollectionBudget(0);

        // Act
        var claimed = budget.TryClaim();

        // Assert
        Assert.False(claimed);
        Assert.Equal(0, budget.Recorded);
    }

    /// <summary>An account synchronizes several folders at once, so the ceiling is the run's rather than each folder's.</summary>
    [Fact]
    public async Task TryClaim_ClaimedFromSeveralFoldersAtOnce_AdmitsTheCeilingOnceForTheWholeRun()
    {
        // Arrange
        const int Ceiling = 50;
        var budget = new ContactCollectionBudget(Ceiling);

        // Act
        var folders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => Enumerable.Range(0, 40).Count(_ => budget.TryClaim()))));

        // Assert
        Assert.Equal(Ceiling, folders.Sum());
        Assert.Equal(Ceiling, budget.Recorded);
    }

    /// <summary>A negative ceiling is a configuration nobody can act on, so it is refused where it is stated.</summary>
    [Fact]
    public void Constructor_ANegativeCeiling_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCollectionBudget(-1));
    }
}
