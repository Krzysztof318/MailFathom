// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Redaction;

/// <summary>Covers the one placeholder scheme every consumer shares.</summary>
public sealed class SensitiveContentPlaceholderTests
{
    private static readonly SensitiveContentCategory CloudKey = SensitiveContentCategory.Create("CloudKey");

    [Fact]
    public void For_Category_NamesTheCategoryAndNothingElse()
    {
        // Act
        var placeholder = SensitiveContentPlaceholder.For(CloudKey);

        // Assert
        Assert.Equal("[redacted:CloudKey]", placeholder);
    }

    /// <summary>A citation drawn from a redacted chunk has to land on the same text a reader is served, so the two routes produce one string.</summary>
    [Fact]
    public void For_FindingAndItsCategory_ProduceTheSamePlaceholder()
    {
        // Arrange
        var finding = SensitiveContentFinding.Create(
            SensitiveContentRule.Create(CloudKey, "cloud-access-key"),
            SensitiveContentSpan.Create(0, 10),
            1,
            SensitiveContentDetector.Create("in-process-secrets", "2026.08.01"),
            DateTimeOffset.UnixEpoch);

        // Act, Assert
        Assert.Equal(SensitiveContentPlaceholder.For(CloudKey), SensitiveContentPlaceholder.For(finding));
    }

    [Fact]
    public void For_NothingToName_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlaceholder.For((SensitiveContentCategory)null!));
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlaceholder.For((SensitiveContentFinding)null!));
    }
}
