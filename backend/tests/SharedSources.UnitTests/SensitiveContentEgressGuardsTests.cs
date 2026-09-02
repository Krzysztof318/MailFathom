// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the guard a consumer of an egress point meets when both switches are off.</summary>
public sealed class SensitiveContentEgressGuardsTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    /// <summary>A deployment with both switches off has no redactor at all, which is what a consumer must see.</summary>
    [Fact]
    public async Task Inactive_ADeploymentWithBothSwitchesOff_GuardsNothingAndReportsItself()
    {
        // Arrange
        var guard = SensitiveContentEgressGuards.Inactive();

        // Act
        var guarded = await guard.GuardAsync(
            SensitiveContentEgressPoint.McpSnippet,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(guard.IsActive);
        Assert.Equal($"the key is {Marker}", guarded);
    }

    /// <summary>Each call builds its own, so a suite reusing the helper cannot leak one test's recorder into another's.</summary>
    [Fact]
    public void Inactive_CalledTwice_BuildsTwoGuards()
    {
        // Act
        var first = SensitiveContentEgressGuards.Inactive();
        var second = SensitiveContentEgressGuards.Inactive();

        // Assert
        Assert.NotSame(first, second);
    }
}
