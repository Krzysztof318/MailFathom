// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Policies;

public sealed class GovernedUserJudgementsTests
{
    private static readonly UserEndpointAccess Nowhere = new(McpEndpoint: false, ClientEndpoint: false);

    private static readonly UserEndpointAccess ClientOnly = new(McpEndpoint: false, ClientEndpoint: true);

    [Fact]
    public void FindRecordsInTheWay_NoRecordRefusingTheWrite_AnswersNoRefusal()
    {
        // Arrange
        StoredGovernedUserRow[] users = [Row(1), Row(2)];
        UserRecordPolicyJudgement[] judgements = [Served(UserEndpointAccess.Everywhere), Served(Nowhere)];

        // Act
        var refusal = GovernedUserJudgements.FindRecordsInTheWay(users, judgements);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>The refusal names who stands in the way and what each would be left without, and commits nothing.</summary>
    [Fact]
    public void FindRecordsInTheWay_ARecordThePolicyWouldLeaveUnservable_IsNamedWithWhatItWouldBeLeftWithout()
    {
        // Arrange
        StoredGovernedUserRow[] users = [Row(1), Row(2), Row(3)];
        UserRecordPolicyJudgement[] judgements =
        [
            Served(UserEndpointAccess.Everywhere),
            new(UserEndpointAccess.Everywhere, ["Language is not stated."]),
            Served(UserEndpointAccess.Everywhere),
        ];

        // Act
        var refusal = GovernedUserJudgements.FindRecordsInTheWay(users, judgements);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SettingsPolicyCommitResult.RecordsLeftInvalid, refusal.Result);
        Assert.Equal(3, refusal.GovernedRecords);
        Assert.Equal(1, refusal.RecordsLeftInvalid);

        var named = Assert.Single(refusal.RecordsInTheWay);

        Assert.Equal(users[1].Id, named.Identity);
        Assert.Equal("user 2", named.DisplayName);
        Assert.Equal(["Language is not stated."], named.Corrections);
    }

    /// <summary>The answer is bounded however many records refuse, and says how many there are in all so the bound is not read as the count.</summary>
    [Fact]
    public void FindRecordsInTheWay_MoreRecordsInTheWayThanOneAnswerNames_NamesTheFirstAndCountsThemAll()
    {
        // Arrange
        var refusing = SettingsPolicyCommit.MaximumRecordsNamed + 3;
        StoredGovernedUserRow[] users = [.. Enumerable.Range(1, refusing).Select(Row)];
        UserRecordPolicyJudgement[] judgements =
        [
            .. users.Select(_ => new UserRecordPolicyJudgement(EndpointAccess: null, ["TimeZone states a zone nobody knows."])),
        ];

        // Act
        var refusal = GovernedUserJudgements.FindRecordsInTheWay(users, judgements);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(refusing, refusal.RecordsLeftInvalid);
        Assert.Equal(
            users.Take(SettingsPolicyCommit.MaximumRecordsNamed).Select(user => user.Id),
            refusal.RecordsInTheWay.Select(named => named.Identity));
    }

    /// <summary>What a request authenticates against is the row, so the rows a policy moves are exactly the ones whose switches differ from what their record now resolves to.</summary>
    [Fact]
    public void FindMovedSwitches_RowsWhoseEffectiveSwitchesThePolicyMoves_AreGroupedByTheSwitchesTheyAreToCarry()
    {
        // Arrange
        StoredGovernedUserRow[] users = [Row(1), Row(2), Row(3), Row(4)];
        UserRecordPolicyJudgement[] judgements =
        [
            Served(Nowhere),
            Served(UserEndpointAccess.Everywhere),
            Served(ClientOnly),
            Served(Nowhere),
        ];

        // Act
        var moved = GovernedUserJudgements.FindMovedSwitches(users, judgements);

        // Assert
        Assert.Equal(2, moved.Count);
        Assert.Equal([users[0].Id, users[3].Id], moved[Nowhere]);
        Assert.Equal([users[2].Id], moved[ClientOnly]);
    }

    /// <summary>A record this build could not resolve keeps the switches its row has, because writing a guess would move somebody on or off an endpoint on no reading at all.</summary>
    [Fact]
    public void FindMovedSwitches_ARecordThatCouldNotBeResolved_LeavesItsRowAsItStands()
    {
        // Arrange
        StoredGovernedUserRow[] users = [Row(1)];
        UserRecordPolicyJudgement[] judgements = [new(EndpointAccess: null, [])];

        // Act
        var moved = GovernedUserJudgements.FindMovedSwitches(users, judgements);

        // Assert
        Assert.Empty(moved);
    }

    /// <summary>A judge that answered for fewer records than it was handed would leave some of them unjudged, which a policy write never is.</summary>
    [Fact]
    public void FindRecordsInTheWay_FewerJudgementsThanRecords_IsRefusedRatherThanReadAsNoRefusal()
    {
        // Arrange
        StoredGovernedUserRow[] users = [Row(1), Row(2)];
        UserRecordPolicyJudgement[] judgements = [Served(UserEndpointAccess.Everywhere)];

        // Act
        var refusal = Record.Exception(() => GovernedUserJudgements.FindRecordsInTheWay(users, judgements));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    private static UserRecordPolicyJudgement Served(UserEndpointAccess access) => new(access, []);

    /// <summary>A row served everywhere, which is what a user's row carries until something moves it.</summary>
    private static StoredGovernedUserRow Row(int number) => new()
    {
        Id = new Guid($"00000000-0000-4000-8000-{number:D12}"),
        DisplayName = $"user {number}",
        Document = "{}",
        McpEndpointEnabled = true,
        ClientEndpointEnabled = true,
    };
}
