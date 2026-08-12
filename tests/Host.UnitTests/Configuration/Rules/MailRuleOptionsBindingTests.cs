// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Rules;
using MailFathom.Host.Configuration.Rules;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what the binder makes of a rule's <c>Triggers</c> key, which is what decides when the rule runs.</summary>
/// <remarks>
/// A rule takes part in the occasions it names and in no others, so an absent key and a written <c>[]</c> have to
/// arrive as the same thing — a rule only a whole-mailbox run applies. The names are bound as text so that one this
/// system cannot read reaches validation instead of being dropped into a shorter list, and this is where both claims
/// are checked rather than assumed: nothing else in the suite would notice the day either stopped holding.
/// </remarks>
public sealed class MailRuleOptionsBindingTests
{
    [Fact]
    public void Bind_ARuleWithNoTriggersKey_LeavesItRunByNoAutomaticOccasion()
    {
        // Act
        var bound = BindRules("""{ "MailRules": { "Rules": [ { "Name": "says-nothing", "Condition": "isSeen" } ] } }""");

        // Assert
        Assert.Empty(bound.Rules[0].Triggers);
        Assert.Empty(bound.Rules[0].ToTriggers());
    }

    [Fact]
    public void Bind_ARuleDeclaringAnEmptyTriggerList_SaysWhatAnAbsentKeySays()
    {
        // Act
        var bound = BindRules(
            """{ "MailRules": { "Rules": [ { "Name": "housekeeping", "Condition": "isSeen", "Triggers": [] } ] } }""");

        // Assert
        Assert.Empty(bound.Rules[0].Triggers);
        Assert.Empty(bound.Rules[0].ToTriggers());
    }

    [Fact]
    public void Bind_ARuleDeclaringTriggers_ReadsEveryNameAsItWasWritten()
    {
        // Act
        var bound = BindRules(
            """
            {
              "MailRules": {
                "Rules": [ { "Name": "on-arrival", "Condition": "isSeen", "Triggers": [ "Arrival", "Schedule" ] } ]
              }
            }
            """);

        // Assert
        Assert.Equal(["Arrival", "Schedule"], bound.Rules[0].Triggers);
        Assert.Equal([MailRuleTrigger.Arrival], bound.Rules[0].ToTriggers());
    }

    /// <summary>The section binds strictly, so a key nothing declares fails rather than being read past.</summary>
    private static MailRulesOptions BindRules(string document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));

        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        return configuration
            .GetSection(MailRulesOptions.SectionName)
            .Get<MailRulesOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)!;
    }
}
