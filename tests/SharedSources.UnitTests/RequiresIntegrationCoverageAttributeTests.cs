// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MailMcp.CodeCoverage;
using MailMcp.Infrastructure.Secrets;
using Xunit;

namespace MailMcp.SharedSources.UnitTests;

/// <summary>Keeps the integration-coverage marker aligned with the coverage collector that reads it.</summary>
public sealed class RequiresIntegrationCoverageAttributeTests
{
    /// <summary>
    /// The collector matches the marker by name through the <c>ExcludeByAttribute</c> entry in <c>testconfig.json</c>,
    /// which no rename refactoring updates. A renamed attribute would still compile and would silently pull every
    /// marked type back into the measured denominator, so the name is asserted here.
    /// </summary>
    [Fact]
    public void AttributeName_ConfiguredCoverageExclusion_MatchesTheCollectedName()
    {
        // Arrange
        const string ConfiguredExclusionName = "RequiresIntegrationCoverageAttribute";

        // Act
        var attributeName = nameof(RequiresIntegrationCoverageAttribute);

        // Assert
        Assert.Equal(ConfiguredExclusionName, attributeName);
    }

    [Fact]
    public void AttributeUsage_MarkedElement_CoversTypesAndMembersWithoutInheritance()
    {
        // Arrange
        const AttributeTargets ExpectedTargets =
            AttributeTargets.Class |
            AttributeTargets.Struct |
            AttributeTargets.Method |
            AttributeTargets.Constructor |
            AttributeTargets.Property;

        // Act
        var usage = typeof(RequiresIntegrationCoverageAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(ExpectedTargets, usage.ValidOn);
        Assert.False(usage.Inherited);
    }

    /// <summary>
    /// The two attributes state different things: one defers verification to the integration suite, the other says the
    /// code never participates in coverage. Carrying both leaves the reason for the exclusion unreadable.
    /// </summary>
    /// <remarks>
    /// The marker is matched by name rather than through <c>typeof</c>. Every assembly that applies it compiles its own
    /// copy from <c>src/shared</c>, so the type this project compiles and the type <c>Infrastructure</c> applies are
    /// distinct to the runtime: <c>IsDefined(typeof(RequiresIntegrationCoverageAttribute))</c> would find nothing and
    /// the assertion would pass without ever inspecting a marked type. Matching by name is also exactly how the
    /// coverage collector recognizes the marker, so this test and the gate read the code the same way.
    /// </remarks>
    [Fact]
    public void MarkedElements_AcrossAnApplyingBoundary_AreNotAlsoExcludedFromCodeCoverage()
    {
        // Arrange
        var applyingBoundaryTypes = typeof(ISecretReferenceResolver).Assembly.GetTypes();

        // Act
        var markedTypes = applyingBoundaryTypes.Where(IsMarkedForIntegrationCoverage).ToArray();
        var doublyMarkedTypeNames = markedTypes
            .Where(type => type.IsDefined(typeof(ExcludeFromCodeCoverageAttribute), inherit: false))
            .Select(type => type.FullName);

        // Assert
        Assert.NotEmpty(markedTypes);
        Assert.Empty(doublyMarkedTypeNames);
    }

    private static bool IsMarkedForIntegrationCoverage(Type type) =>
        type.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == nameof(RequiresIntegrationCoverageAttribute));
}
