// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Domain.UnitTests.Failures;

/// <summary>Covers the five-digit error-code contract a boundary publishes.</summary>
public sealed class MailFathomErrorCodeTests
{
    /// <summary>Two failures sharing a code would be indistinguishable in every log and every error response.</summary>
    [Fact]
    public void All_CodesAreUnique()
    {
        // Act
        var distinctValues = MailFathomErrorCode.All.Select(code => code.Value).Distinct().Count();

        // Assert
        Assert.Equal(MailFathomErrorCode.All.Count, distinctValues);
    }

    /// <summary>
    /// A declared code left out of the registry is invisible to every other assertion here, because they all iterate
    /// the registry. It is also silently unpublishable: <see cref="MailFathomErrorCode.TryParse" /> and JSON reading
    /// resolve a number through the registry alone, so a boundary would reject the very code it just raised.
    /// </summary>
    [Fact]
    public void All_ListsEveryDeclaredCode()
    {
        // Arrange
        var declaredCodes = typeof(MailFathomErrorCode)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(MailFathomErrorCode))
            .Select(property => (MailFathomErrorCode)property.GetValue(null)!);

        // Act
        var unregistered = declaredCodes.Where(code => !MailFathomErrorCode.All.Contains(code)).ToArray();

        // Assert
        Assert.Empty(unregistered);
    }

    /// <summary>A code shorter or longer than five digits would not decompose into a category and a subcategory.</summary>
    [Fact]
    public void All_CodesAreFiveDigits()
    {
        // Act
        var outsideTheRange = MailFathomErrorCode.All.Where(code => code.Value is < 10000 or > 99999).ToArray();

        // Assert
        Assert.Empty(outsideTheRange);
    }

    [Theory]
    [InlineData(11001, 1, 1)]
    [InlineData(12001, 1, 2)]
    [InlineData(21001, 2, 1)]
    [InlineData(22001, 2, 2)]
    [InlineData(23001, 2, 3)]
    [InlineData(31001, 3, 1)]
    [InlineData(35001, 3, 5)]
    [InlineData(41001, 4, 1)]
    [InlineData(51001, 5, 1)]
    [InlineData(51002, 5, 1)]
    [InlineData(52001, 5, 2)]
    [InlineData(53001, 5, 3)]
    [InlineData(81001, 8, 1)]
    public void CategoryAndSubcategory_AreTheFirstTwoDigits(int allocatedValue, int expectedCategory, int expectedSubcategory)
    {
        // Arrange
        var code = Assert.Single(MailFathomErrorCode.All, allocated => allocated.Value == allocatedValue);

        // Assert
        Assert.Equal(expectedCategory, code.Category);
        Assert.Equal(expectedSubcategory, code.Subcategory);
    }

    [Fact]
    public void TryParse_AllocatedNumber_ReturnsTheCode()
    {
        // Act
        var parsed = MailFathomErrorCode.TryParse(22001, out var code);

        // Assert
        Assert.True(parsed);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, code);
    }

    /// <summary>A retired or mistyped number is unknown rather than reconstructed as a value nothing raises.</summary>
    [Fact]
    public void TryParse_UnallocatedNumber_ReportsUnspecified()
    {
        // Act
        var parsed = MailFathomErrorCode.TryParse(99999, out var code);

        // Assert
        Assert.False(parsed);
        Assert.False(code.IsSpecified);
    }

    [Fact]
    public void Default_NamesNoFailure()
    {
        // Arrange
        var code = default(MailFathomErrorCode);

        // Assert
        Assert.False(code.IsSpecified);
        Assert.Equal("(unspecified)", code.ToString());
        Assert.Throws<InvalidOperationException>(() => code.Category);
        Assert.Throws<InvalidOperationException>(() => code.Subcategory);
    }

    /// <summary>A log or an error response records the number, not the structure that carries it.</summary>
    [Fact]
    public void ToString_IsTheFiveDigitNumber()
    {
        // Act
        var recorded = MailFathomErrorCode.MailboxUnavailable.ToString();

        // Assert
        Assert.Equal("22001", recorded);
    }

    [Fact]
    public void JsonRoundTrip_PreservesTheCode()
    {
        // Act
        var json = JsonSerializer.Serialize(MailFathomErrorCode.MailboxFolderRecreated);
        var restored = JsonSerializer.Deserialize<MailFathomErrorCode>(json);

        // Assert
        Assert.Equal("23001", json);
        Assert.Equal(MailFathomErrorCode.MailboxFolderRecreated, restored);
    }

    [Fact]
    public void JsonRoundTrip_AsAPropertyName_PreservesTheCode()
    {
        // Arrange
        var codes = new Dictionary<MailFathomErrorCode, string> { [MailFathomErrorCode.MailboxUnavailable] = "retry later" };

        // Act
        var json = JsonSerializer.Serialize(codes);
        var restored = JsonSerializer.Deserialize<Dictionary<MailFathomErrorCode, string>>(json);

        // Assert
        Assert.Equal("{\"22001\":\"retry later\"}", json);
        Assert.Equal("retry later", Assert.Single(restored!).Value);
    }

    [Theory]
    [InlineData("\"22001\"")]
    [InlineData("99999")]
    public void JsonRead_TokenThatNamesNoAllocatedCode_IsRejected(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailFathomErrorCode>(json));
    }

    /// <summary>A key the converter never writes must not read back as a code, or two spellings would name one failure.</summary>
    [Theory]
    [InlineData("{\"022001\":\"padded\"}")]
    [InlineData("{\"2201\":\"too short\"}")]
    [InlineData("{\"+22001\":\"signed\"}")]
    [InlineData("{\"22001 \":\"trailing space\"}")]
    public void JsonRead_PropertyNameThatIsNotTheCanonicalFiveDigits_IsRejected(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<MailFathomErrorCode, string>>(json));
    }

    [Fact]
    public void JsonWrite_UnspecifiedCode_IsRejected()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(MailFathomErrorCode)));
    }
}
