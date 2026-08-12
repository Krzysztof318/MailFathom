// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Rules;
using MailFathom.Host.Configuration.Rules;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers what the binder makes of a rule's <c>Triggers</c> key, which is what its two readings rest on.</summary>
/// <remarks>
/// A written <c>[]</c> reaches the binder as a key holding an empty value and no children, and a list property left
/// alone by that is indistinguishable from one nobody wrote. The key is therefore an array, which the binder rebuilds
/// from the section, and this is where that claim is checked rather than assumed — the whole meaning of a manual-only
/// rule depends on it, and nothing else in the suite would notice the day it stopped holding.
/// </remarks>
public sealed class MailRuleOptionsBindingTests
{
    [Fact]
    public void Bind_ARuleWithNoTriggersKey_LeavesTheTriggersUndeclared()
    {
        // Act
        var bound = BindRules("""{ "MailRules": { "Rules": [ { "Name": "says-nothing", "Condition": "isSeen" } ] } }""");

        // Assert
        Assert.Null(bound.Rules[0].Triggers);
        Assert.Equal(MailRuleTrigger.WhenNoneDeclared, bound.Rules[0].ToTriggers());
    }

    [Fact]
    public void Bind_ARuleDeclaringAnEmptyTriggerList_KeepsItApartFromDeclaringNone()
    {
        // Act
        var bound = BindRules(
            """{ "MailRules": { "Rules": [ { "Name": "housekeeping", "Condition": "isSeen", "Triggers": [] } ] } }""");

        // Assert
        Assert.NotNull(bound.Rules[0].Triggers);
        Assert.Empty(bound.Rules[0].Triggers!);
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
        Assert.Equal(["Arrival", "Schedule"], bound.Rules[0].Triggers ?? []);
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
