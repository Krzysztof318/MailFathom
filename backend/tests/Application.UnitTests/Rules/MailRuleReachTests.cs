// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers which rules each of the two kinds of walk runs.</summary>
public sealed class MailRuleReachTests
{
    [Fact]
    public void Reaches_ATriggeredWalk_RunsOnlyTheRulesDeclaringThatTrigger()
    {
        // Arrange
        var reach = MailRuleReach.TriggeredBy(MailRuleTrigger.Arrival);

        // Act, Assert
        Assert.True(reach.Reaches(CreateRule("on-arrival", [MailRuleTrigger.Arrival])));
        Assert.False(reach.Reaches(CreateRule("manual-only", [])));
        Assert.False(reach.Reaches(CreateRule("says-nothing", triggers: null)));
    }

    /// <summary>Asking for a run is the request itself, so no rule can decline to take part in one.</summary>
    [Fact]
    public void Reaches_AWalkSomebodyAskedFor_RunsEveryRule()
    {
        // Act, Assert
        Assert.True(MailRuleReach.EveryRule.Reaches(CreateRule("on-arrival", [MailRuleTrigger.Arrival])));
        Assert.True(MailRuleReach.EveryRule.Reaches(CreateRule("manual-only", [])));
    }

    [Fact]
    public void TriggeredBy_TheUnspecifiedDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleReach.TriggeredBy(default));
    }

    [Fact]
    public void ToString_EitherKindOfWalk_NamesWhatItRuns()
    {
        // Act, Assert
        Assert.Equal("Arrival", MailRuleReach.TriggeredBy(MailRuleTrigger.Arrival).ToString());
        Assert.Equal("every rule", MailRuleReach.EveryRule.ToString());
    }

    private static MailRule CreateRule(string name, IReadOnlyList<MailRuleTrigger>? triggers) => MailRule.Create(
        name,
        ScriptedMailRuleCondition.Answering(matches: true),
        triggers: triggers);
}
