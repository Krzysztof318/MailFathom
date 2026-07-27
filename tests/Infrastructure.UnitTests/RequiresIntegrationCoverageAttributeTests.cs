// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.CodeCoverage;
using MailMcp.Infrastructure.Secrets;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>
/// Keeps this boundary's use of the integration-coverage marker honest. The marker's own contract — its name and its
/// usage targets — is asserted in <c>SharedSources.UnitTests</c>, alongside the shared source that declares it.
/// </summary>
public sealed class RequiresIntegrationCoverageAttributeTests
{
    /// <summary>
    /// The two attributes state different things: one defers verification to the integration suite, the other says the
    /// code never participates in coverage. Carrying both leaves the reason for the exclusion unreadable.
    /// </summary>
    [Fact]
    public void InfrastructureTypes_MarkedForIntegrationCoverage_AreNotAlsoExcludedFromCodeCoverage()
    {
        // Arrange
        var infrastructureTypes = typeof(ISecretReferenceResolver).Assembly.GetTypes();

        // Act
        var doublyMarkedTypeNames = infrastructureTypes
            .Where(type => type.IsDefined(typeof(RequiresIntegrationCoverageAttribute), inherit: false)
                && type.IsDefined(typeof(ExcludeFromCodeCoverageAttribute), inherit: false))
            .Select(type => type.FullName);

        // Assert
        Assert.Empty(doublyMarkedTypeNames);
    }
}
