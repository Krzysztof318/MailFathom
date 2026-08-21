// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Detection;

/// <summary>Covers what a scanner may declare about a category, since every configured name is judged against it.</summary>
public sealed class SensitiveContentCategoryDefinitionTests
{
    private static readonly SensitiveContentCategory CloudKey = SensitiveContentCategory.Create("CloudKey");

    [Fact]
    public void Create_Declaration_CarriesTheDefaultMembershipAndTheRules()
    {
        // Arrange
        var rules = new[]
        {
            SensitiveContentRule.Create(CloudKey, "aws-access-token"),
            SensitiveContentRule.Create(CloudKey, "gcp-api-key"),
        };

        // Act
        var definition = SensitiveContentCategoryDefinition.Create(CloudKey, detectedByDefault: true, rules);

        // Assert
        Assert.Equal(CloudKey, definition.Category);
        Assert.True(definition.DetectedByDefault);
        Assert.Equal(rules, definition.Rules);
    }

    [Fact]
    public void Create_CategoryWithNoRule_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentCategoryDefinition.Create(CloudKey, true, []));
    }

    /// <summary>A rule declared under the wrong category would let a suppression name a rule the category does not hold.</summary>
    [Fact]
    public void Create_RuleBelongingToAnotherCategory_IsRejected()
    {
        // Arrange
        var elsewhere = SensitiveContentRule.Create(SensitiveContentCategory.Create("PersonName"), "person");

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentCategoryDefinition.Create(CloudKey, true, [elsewhere]));
    }

    [Fact]
    public void Create_SameRuleNameTwice_IsRejected()
    {
        // Arrange
        var rules = new[]
        {
            SensitiveContentRule.Create(CloudKey, "aws-access-token"),
            SensitiveContentRule.Create(CloudKey, "AWS-Access-Token"),
        };

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentCategoryDefinition.Create(CloudKey, true, rules));
    }
}
