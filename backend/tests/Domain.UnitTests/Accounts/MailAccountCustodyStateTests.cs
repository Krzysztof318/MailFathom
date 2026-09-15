// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Accounts;

/// <summary>
/// The pair decides whether a message gone from the source is somebody's deletion or the drain's own work, which is the
/// one reading that erases mail when it is wrong. That is what these cover.
/// </summary>
public sealed class MailAccountCustodyStateTests
{
    [Fact]
    public void Mirrored_AnAccountNobodyHasSwitched_MirrorsItsSourceAndWaitsForNothing()
    {
        // Act
        var state = MailAccountCustodyState.Mirrored;

        // Assert
        Assert.Equal(MailAccountCustody.MirrorSource, state.Requested);
        Assert.Equal(MailAccountCustodyPhase.Mirrored, state.Phase);
        Assert.False(state.IsSwitchPending);
    }

    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored, true)]
    [InlineData(MailAccountCustodyPhase.Held, false)]
    [InlineData(MailAccountCustodyPhase.Restoring, false)]
    public void AppliesRemoteDeletions_EachPhase_AnswersFromThePhaseAndNotFromWhatWasRequested(
        MailAccountCustodyPhase phase,
        bool applies)
    {
        // Arrange
        var state = new MailAccountCustodyState(MailAccountCustody.MirrorSource, phase);

        // Act and assert
        Assert.Equal(applies, state.AppliesRemoteDeletions);
    }

    /// <summary>
    /// A restoring account is still answering from what MailFathom stores while it appends the mailbox back, so the
    /// source does not become the truth until the phase is mirrored again.
    /// </summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored, false)]
    [InlineData(MailAccountCustodyPhase.Held, true)]
    [InlineData(MailAccountCustodyPhase.Restoring, true)]
    public void IsMailFathomTheTruth_EachPhase_IsTrueForEveryPhaseTheSwitchPassesThrough(
        MailAccountCustodyPhase phase,
        bool isTheTruth)
    {
        // Arrange
        var state = new MailAccountCustodyState(MailAccountCustody.HoldMailbox, phase);

        // Act and assert
        Assert.Equal(isTheTruth, state.IsMailFathomTheTruth);
    }

    [Theory]
    [InlineData(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Mirrored, true)]
    [InlineData(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Restoring, true)]
    [InlineData(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held, false)]
    [InlineData(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Held, true)]
    [InlineData(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Restoring, true)]
    [InlineData(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Mirrored, false)]
    public void IsSwitchPending_EachPairing_IsPendingUntilThePhaseReachesWhatWasAskedFor(
        MailAccountCustody requested,
        MailAccountCustodyPhase phase,
        bool pending)
    {
        // Arrange
        var state = new MailAccountCustodyState(requested, phase);

        // Act and assert
        Assert.Equal(pending, state.IsSwitchPending);
    }
}
