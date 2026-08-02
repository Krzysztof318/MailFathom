// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.CodeCoverage;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Keeps the integration-coverage marker aligned with the coverage collector that reads it.</summary>
public sealed class RequiresIntegrationCoverageAttributeTests
{
    /// <summary>
    /// The collector matches the marker by name through the <c>ExcludeByAttribute</c> entry in <c>.config/testconfig.json</c>,
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
    /// The collector reads the marker off individual elements, so a marker that bound only to types would silently
    /// leave a marked method or property in the measured denominator.
    /// </summary>
    [Fact]
    public void MarkedElements_TypeAndItsMembers_AreDiscoverableUnderTheCollectedName()
    {
        // Arrange
        var sampleType = typeof(IntegrationVerifiedSample);

        // Act
        var markedElementNames = sampleType.GetMembers()
            .Cast<MemberInfo>()
            .Prepend(sampleType)
            .Where(IsMarkedForIntegrationCoverage)
            .Select(element => element.Name);

        // Assert
        Assert.Equal(
            [
                nameof(IntegrationVerifiedSample.Connect),
                nameof(IntegrationVerifiedSample),
                nameof(IntegrationVerifiedSample.IsConnected),
            ],
            markedElementNames.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The collector matches the marker by name rather than by declaring assembly, because every consumer compiles its
    /// own copy from <c>src/shared</c>. Reading it the same way here keeps this test and the gate in agreement.
    /// </summary>
    private static bool IsMarkedForIntegrationCoverage(MemberInfo element) =>
        element.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == nameof(RequiresIntegrationCoverageAttribute));
}
