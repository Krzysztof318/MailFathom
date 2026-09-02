// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.ObjectStorage;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers the classification an object-storage failure is published under, in a metric and in an error code.</summary>
public sealed class ObjectStorageFailureTests
{
    /// <summary>Two classifications sharing a name would be one series in every dashboard that splits by it.</summary>
    [Fact]
    public void All_NamesAreUnique()
    {
        // Act
        var distinctNames = ObjectStorageFailure.All
            .Select(failure => failure.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();

        // Assert
        Assert.Equal(ObjectStorageFailure.All.Count, distinctNames);
    }

    /// <summary>A code shared by two classifications would leave an alert unable to tell a refused credential from an unreachable endpoint.</summary>
    [Fact]
    public void All_ErrorCodesAreUniqueAndBelongToTheObjectStorageSubcategory()
    {
        // Act
        var codes = ObjectStorageFailure.All.Select(failure => failure.ErrorCode).ToArray();

        // Assert
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(
            codes,
            code =>
            {
                Assert.Equal(3, code.Category);
                Assert.Equal(6, code.Subcategory);
            });
    }

    /// <summary>The published name is snake_case, because that is the shape every other dimension word in this system carries.</summary>
    [Fact]
    public void All_NamesAreLowerCaseWordsSeparatedByUnderscores()
    {
        // Act
        var irregular = ObjectStorageFailure.All
            .Select(failure => failure.Name)
            .Where(name => !name.All(character => char.IsAsciiLetterLower(character) || character == '_'))
            .ToArray();

        // Assert
        Assert.Empty(irregular);
    }

    /// <summary>What may be attempted again is the whole reason the classification exists, so each member's verdict is stated rather than derived.</summary>
    [Theory]
    [InlineData("caller_cancelled", false)]
    [InlineData("host_shutting_down", false)]
    [InlineData("timed_out", true)]
    [InlineData("authentication_failed", false)]
    [InlineData("transient_transport_failure", true)]
    [InlineData("unrecognized", false)]
    public void IsWorthRepeating_EachClassification_CarriesTheVerdictThePipelineReads(string name, bool expected)
    {
        // Arrange
        var failure = Assert.Single(ObjectStorageFailure.All, candidate => candidate.Name == name);

        // Assert
        Assert.Equal(expected, failure.IsWorthRepeating);
    }

    [Fact]
    public void TryParse_DeclaredName_ReturnsThatClassification()
    {
        // Act
        var parsed = ObjectStorageFailure.TryParse("  Authentication_Failed ", out var failure);

        // Assert
        Assert.True(parsed);
        Assert.Equal(ObjectStorageFailure.AuthenticationFailed, failure);
    }

    /// <summary>A name nothing declares is unknown rather than a classification this system just gained.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bucket_on_fire")]
    public void TryParse_UndeclaredName_ReportsUnspecified(string? name)
    {
        // Act
        var parsed = ObjectStorageFailure.TryParse(name, out var failure);

        // Assert
        Assert.False(parsed);
        Assert.False(failure.IsSpecified);
    }

    /// <summary>The struct default is reachable and classifies nothing, so everything that publishes a classification refuses it.</summary>
    [Fact]
    public void Default_ClassifiesNothing()
    {
        // Arrange
        var failure = default(ObjectStorageFailure);

        // Assert
        Assert.False(failure.IsSpecified);
        Assert.Equal("(unspecified)", failure.ToString());
        Assert.Throws<InvalidOperationException>(() => failure.Name);
        Assert.Throws<InvalidOperationException>(() => failure.ErrorCode);
    }

    /// <summary>A log record carries the published name, which is what a dashboard query already matches on.</summary>
    [Fact]
    public void ToString_IsThePublishedName()
    {
        // Assert
        Assert.Equal("transient_transport_failure", ObjectStorageFailure.TransientTransportFailure.ToString());
    }

    [Fact]
    public void JsonRoundTrip_AsAValue_PreservesEveryClassification()
    {
        // Act
        var restored = ObjectStorageFailure.All
            .Select(failure => JsonSerializer.Deserialize<ObjectStorageFailure>(JsonSerializer.Serialize(failure)))
            .ToArray();

        // Assert
        Assert.Equal(ObjectStorageFailure.All, restored);
    }

    [Fact]
    public void JsonRoundTrip_AsAPropertyName_PreservesEveryClassification()
    {
        // Arrange
        var lengthsByClassification = ObjectStorageFailure.All.ToDictionary(
            failure => failure,
            failure => failure.Name.Length);

        // Act
        var restored = JsonSerializer.Deserialize<Dictionary<ObjectStorageFailure, int>>(
            JsonSerializer.Serialize(lengthsByClassification));

        // Assert
        Assert.Equal(lengthsByClassification, restored);
    }

    [Fact]
    public void JsonWrite_TheUnspecifiedDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(ObjectStorageFailure)));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(new Dictionary<ObjectStorageFailure, int> { [default] = 1 }));
    }

    /// <summary>An undeclared name read back would be a classification nothing here can act on.</summary>
    [Theory]
    [InlineData("\"bucket_on_fire\"")]
    [InlineData("17")]
    public void JsonRead_ATokenNamingNoClassification_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ObjectStorageFailure>(json));
    }

    [Fact]
    public void JsonRead_APropertyNameNamingNoClassification_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<ObjectStorageFailure, int>>("{\"bucket_on_fire\":1}"));
    }

    /// <summary>Each classification's code is the one a boundary reports, so an alert matching a number keeps meaning what it meant.</summary>
    [Theory]
    [InlineData("caller_cancelled", 36001)]
    [InlineData("host_shutting_down", 36002)]
    [InlineData("timed_out", 36003)]
    [InlineData("authentication_failed", 36004)]
    [InlineData("transient_transport_failure", 36005)]
    [InlineData("unrecognized", 36006)]
    public void ErrorCode_EachClassification_IsTheAllocatedNumber(string name, int expectedCode)
    {
        // Arrange
        var failure = Assert.Single(ObjectStorageFailure.All, candidate => candidate.Name == name);

        // Assert
        Assert.Equal(expectedCode, failure.ErrorCode.Value);
        Assert.Contains(failure.ErrorCode, MailFathomErrorCode.All);
    }
}
