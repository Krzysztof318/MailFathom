// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobTypeTests
{
    /// <summary>
    /// The set is closed because every enqueuer is in-tree, and a member of it names exactly one payload contract. This
    /// test is what makes appending one a deliberate act rather than a side effect of a consumer arriving.
    /// </summary>
    [Fact]
    public void All_HoldsExactlyTheDeclaredTypes()
    {
        // Arrange
        JobType[] expected =
        [
            JobType.ClassifyEmailSpam,
            JobType.RunScheduledMailRules,
            JobType.RederiveStoredMail,
            JobType.DispatchHeldSend,
            JobType.SendRecurringOccurrence,
        ];

        // Act
        var declared = JobType.All;

        // Assert
        Assert.Equal(expected, declared);
    }

    /// <summary>Two types sharing a name would make a stored row ambiguous about which contract it is read back under.</summary>
    [Fact]
    public void All_NamesEachTypeOnce()
    {
        // Act
        var names = JobType.All.Select(jobType => jobType.Name).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The name is what a log line, a span, a counter dimension, and the stored row all show, so it is the published identity.</summary>
    [Theory]
    [InlineData("classify-email-spam")]
    public void TryParseName_ADeclaredName_ReturnsTheTypeItNames(string name)
    {
        // Act
        var parsed = JobType.TryParseName(name, out var jobType);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, jobType.Name);
    }

    /// <summary>
    /// A name a running build does not declare is what an older replica meets when a newer one introduces a type. It is
    /// read as unknown so the row can be left where it is, rather than reconstructed as a value nothing runs.
    /// </summary>
    [Theory]
    [InlineData("evaluate-rule")]
    [InlineData("generate-embedding")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseName_ANameNothingDeclares_IsUnknownRatherThanReconstructed(string? name)
    {
        // Act
        var parsed = JobType.TryParseName(name, out var jobType);

        // Assert
        Assert.False(parsed);
        Assert.False(jobType.IsSpecified);
    }

    /// <summary>Being a struct, the default is reachable and names nothing; reading it as a name is a defect, not a value.</summary>
    [Fact]
    public void Name_OnTheStructDefault_ThrowsRatherThanNamingAType()
    {
        // Arrange
        var unspecified = default(JobType);

        // Act & Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Name);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    [Fact]
    public void Serialization_OfADeclaredType_RoundTripsThroughItsName()
    {
        // Act
        var json = JsonSerializer.Serialize(JobType.ClassifyEmailSpam);
        var restored = JsonSerializer.Deserialize<JobType>(json);

        // Assert
        Assert.Equal("\"classify-email-spam\"", json);
        Assert.Equal(JobType.ClassifyEmailSpam, restored);
    }

    [Fact]
    public void Serialization_AsAPropertyName_RoundTripsThroughTheSameName()
    {
        // Arrange
        var counts = new Dictionary<JobType, int> { [JobType.ClassifyEmailSpam] = 2 };

        // Act
        var json = JsonSerializer.Serialize(counts);
        var restored = JsonSerializer.Deserialize<Dictionary<JobType, int>>(json);

        // Assert
        Assert.Equal("{\"classify-email-spam\":2}", json);
        Assert.Equal(counts, restored);
    }

    [Fact]
    public void Deserialization_OfANameNothingDeclares_IsRefused()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JobType>("\"evaluate-rule\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JobType>("7"));
    }

    [Fact]
    public void Serialization_OfTheStructDefault_IsRefused()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(JobType)));
    }
}
