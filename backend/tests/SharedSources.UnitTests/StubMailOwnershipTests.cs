// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the ownership answer every suite bounding two owners against each other arranges through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A stub that answered with one owner whatever it was asked would
/// make a per-owner ceiling test pass while the ceiling bounded nothing, which is exactly the claim those tests exist
/// to make; and one that ignored its default would leave every suite that arranges no owner at all refused.
/// </remarks>
public sealed class StubMailOwnershipTests
{
    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.NewGuid());

    [Fact]
    public async Task ReadStoredEmailOwnerAsync_NothingArranged_AnswersWithTheDefaultOwner()
    {
        // Arrange
        var ownership = new StubMailOwnership();

        // Act
        var owner = await ownership.ReadStoredEmailOwnerAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, owner);
    }

    [Fact]
    public async Task ReadStoredEmailOwnerAsync_AMessageArrangedToSomebodyElse_AnswersWithThatOwner()
    {
        // Arrange
        var ownership = new StubMailOwnership().Owns(Message, SyntheticMailOwner.Another);
        var unarranged = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var owner = await ownership.ReadStoredEmailOwnerAsync(Message, TestContext.Current.CancellationToken);
        var fallback = await ownership.ReadStoredEmailOwnerAsync(unarranged, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailOwner.Another, owner);
        Assert.Equal(SyntheticMailOwner.Deployment, fallback);
    }

    /// <summary>The default is the stub's own, so a suite serving somebody other than the deployment states it once.</summary>
    [Fact]
    public async Task ReadStoredEmailOwnerAsync_AStatedDefault_IsWhatAnUnarrangedMessageAnswersWith()
    {
        // Arrange
        var ownership = new StubMailOwnership(SyntheticMailOwner.Another);

        // Act
        var owner = await ownership.ReadStoredEmailOwnerAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailOwner.Another, owner);
    }

    [Fact]
    public async Task ReadStoredEmailOwnerAsync_ACancelledToken_IsObserved()
    {
        // Arrange
        var ownership = new StubMailOwnership();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ownership.ReadStoredEmailOwnerAsync(Message, cancellation.Token));
    }
}
