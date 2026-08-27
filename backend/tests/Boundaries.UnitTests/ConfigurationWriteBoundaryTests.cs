// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using ArchUnitNET.Domain.Extensions;
using Xunit;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers that a configuration value is never assigned in place, whatever holds the configuration.</summary>
/// <remarks>
/// <para>
/// Changing a setting is a transaction, an optimistic-concurrency check, a candidate composed with every source that
/// outranks the persisted layer, and the binding and validators a start applies. <c>configuration["key"] = value</c>
/// is none of those: it mutates a provider's dictionary inside one process, so the value takes effect having been
/// proved by nothing, is lost at the next reload, and reaches no other process at all.
/// <c>IConfigurationWriter</c> is the one way a setting changes, and this is what leaves no second one — the indexer's
/// setter, a section's value, and a provider's own <c>Set</c>, which is the same dictionary one layer down.
/// </para>
/// <para>
/// It reads intermediate language because an assignment is a call inside a method body and nothing about a type's
/// shape records it. The rule is written against the called members rather than through <c>CallAny</c>, because the
/// fluent form resolves its argument among the *loaded* assemblies and every one of these members is declared in the
/// framework.
/// </para>
/// </remarks>
public sealed partial class ConfigurationWriteBoundaryTests
{
    /// <summary>
    /// A read of the same indexer, which the solution genuinely performs. Asserting it is found is what keeps the rule
    /// below from passing because it can see nothing: a pattern that matched no call would otherwise look exactly like
    /// a solution that assigns none.
    /// </summary>
    [GeneratedRegex(@"^[\w.`\[\]<>,& ]+ Microsoft\.Extensions\.Configuration\.[\w.`]+::get_Item\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConfigurationRead { get; }

    [GeneratedRegex(@"^[\w.`\[\]<>,& ]+ Microsoft\.Extensions\.Configuration\.[\w.`]+::(set_Item|set_Value|Set)\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConfigurationWrite { get; }

    [Fact]
    public void ConfigurationValues_AnywhereInTheSolution_AreNeverAssignedInPlace()
    {
        // Arrange
        var calls = CompiledBoundaries.Solution
            .MethodMembers
            .SelectMany(member => member.GetCalledMethods().Select(called => (Caller: member.FullName, called.FullName)))
            .ToArray();

        // Act
        var reads = calls.Where(call => ConfigurationRead.IsMatch(call.FullName)).ToArray();
        var assignments = calls
            .Where(call => ConfigurationWrite.IsMatch(call.FullName))
            .Select(call => $"{call.Caller} calls {call.FullName}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.NotEmpty(reads);
        Assert.True(
            assignments.Length == 0,
            "A setting is changed through IConfigurationWriter, which resolves where the path is persisted, composes "
                + "the configuration the change would produce, runs the binding and the validators a start runs, and "
                + "commits against the version the change was authored over. Assigning a configuration value in place "
                + "proves nothing, persists nothing, and is lost at the next reload:\n"
                + string.Join("\n", assignments));
    }
}
