// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Commands;
using Microsoft.Extensions.Time.Testing;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Commands;

/// <summary>What one invocation is checked for, and what it resolves for itself.</summary>
public sealed class BatchArgumentsTests
{
    private static readonly DateTimeOffset Today = new(2026, 8, 8, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_NoSeed_ChoosesOneAndPrintsAnInvocationThatReproducesIt()
    {
        // Arrange, Act
        var arguments = Parse();

        // Assert
        Assert.Contains($"--seed {arguments.Seed}", arguments.RepeatCommandLine, StringComparison.Ordinal);
        Assert.Contains("--count 50", arguments.RepeatCommandLine, StringComparison.Ordinal);
        Assert.Contains("--days 90", arguments.RepeatCommandLine, StringComparison.Ordinal);
        Assert.Contains("--until 2026-08-08", arguments.RepeatCommandLine, StringComparison.Ordinal);
        Assert.Contains("--attachment-bytes 4096", arguments.RepeatCommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ASeed_KeepsIt()
    {
        // Arrange, Act
        var arguments = Parse(seed: 4711);

        // Assert
        Assert.Equal(4711, arguments.Seed);
    }

    [Fact]
    public void Parse_NoUntil_TakesTodayFromTheClockRatherThanTheWallClock()
    {
        // Arrange, Act
        var arguments = Parse();

        // Assert
        Assert.Equal(new DateOnly(2026, 8, 8), arguments.LatestDate);
        Assert.Equal(new DateOnly(2026, 5, 10), arguments.EarliestDate);
    }

    [Fact]
    public void Parse_AnUntil_DatesTheNewestMessageAtTheEndOfThatDay()
    {
        // Arrange, Act
        var arguments = Parse(until: "2026-03-01");

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 23, 59, 59, TimeSpan.Zero), arguments.LatestSentAt);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2026-3-1")]
    [InlineData("01/03/2026")]
    [InlineData("yesterday")]
    public void Parse_AnUntilThatIsNotADate_IsRefused(string until)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Parse(until: until));

        // Assert
        Assert.Contains("yyyy-MM-dd", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ARangeReachingBeforeTheFirstRepresentableDate_IsRefused()
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Parse(until: "0001-01-01", days: 30));

        // Assert
        // The calendar runs out before the range does, and a mistyped date deserves a sentence rather than the stack
        // trace DateOnly would raise from inside the generator's plan.
        Assert.Contains("first representable date", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("")]
    public void Parse_ARecipientThatIsNotAnAddress_IsRefused(string recipient)
    {
        // Arrange, Act, Assert
        Assert.Throws<SyntheticMailFailure>(() => Parse(recipient: recipient));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BatchArguments.MaximumCount + 1)]
    public void Parse_ACountOutsideItsBounds_IsRefusedNamingTheOption(int count)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Parse(count: count));

        // Assert
        Assert.Contains("--count", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"1..{BatchArguments.MaximumCount}", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(BatchArguments.MaximumSpanDays + 1)]
    public void Parse_ASpanOutsideItsBounds_IsRefusedNamingTheOption(int days)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Parse(days: days));

        // Assert
        Assert.Contains("--days", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(BatchArguments.MaximumAttachmentCeiling + 1)]
    public void Parse_AnAttachmentCeilingOutsideItsBounds_IsRefusedNamingTheOption(int attachmentBytes)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Parse(attachmentBytes: attachmentBytes));

        // Assert
        Assert.Contains("--attachment-bytes", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(BatchArguments.MaximumSensitivePercentage + 1)]
    public void Parse_ASensitiveShareOutsideItsBounds_IsRefusedNamingTheOption(int sensitivePercentage)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(
            () => Parse(sensitivePercentage: sensitivePercentage));

        // Assert
        Assert.Contains("--sensitive-percentage", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(BatchArguments.MaximumSensitivePercentage)]
    public void Parse_ASensitiveShareAtEitherEndOfItsBounds_IsAccepted(int sensitivePercentage)
    {
        // Arrange, Act
        var arguments = Parse(sensitivePercentage: sensitivePercentage);

        // Assert
        // Both ends are answers rather than mistakes: a corpus with nothing to find and one where every message
        // carries something are the two runs a scanner is compared between.
        Assert.Equal(sensitivePercentage, arguments.SensitivePercentage);
        Assert.Equal(sensitivePercentage, arguments.ToPlan().SensitivePercentage);
    }

    [Fact]
    public void RepeatCommandLine_Always_CarriesTheSensitiveShareTheRunUsed()
    {
        // Arrange, Act
        var arguments = Parse(sensitivePercentage: 35);

        // Assert
        Assert.Contains("--sensitive-percentage 35", arguments.RepeatCommandLine, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(BatchArguments.MaximumIntervalMilliseconds + 1)]
    public void Parse_AnIntervalOutsideItsBounds_IsRefusedNamingTheOption(int intervalMilliseconds)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Parse(intervalMilliseconds: intervalMilliseconds));

        // Assert
        Assert.Contains("--interval", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NoConfigurationPath_ReadsTheCredentialBesideTheBuiltCommand()
    {
        // Arrange, Act
        var arguments = Parse();

        // Assert
        Assert.EndsWith("synthetic-mail.local.json", arguments.ConfigurationPath, StringComparison.Ordinal);
        Assert.True(Path.IsPathRooted(arguments.ConfigurationPath));
    }

    [Fact]
    public void ToPlan_AnInvocation_CarriesEveryValueTheGeneratorReads()
    {
        // Arrange
        var arguments = Parse(seed: 12, count: 7, until: "2026-01-31", days: 14, attachmentBytes: 2048);

        // Act
        var plan = arguments.ToPlan();

        // Assert
        Assert.Equal(12, plan.Seed);
        Assert.Equal(7, plan.Count);
        Assert.Equal(14, plan.SpanDays);
        Assert.Equal(2048, plan.MaximumAttachmentBytes);
        Assert.Equal(new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero), plan.LatestSentAt);
    }

    private static BatchArguments Parse(
        string recipient = "developer@example.com",
        int? seed = null,
        int count = 50,
        string? until = null,
        int days = 90,
        int attachmentBytes = 4096,
        int sensitivePercentage = 20,
        int intervalMilliseconds = 0) => BatchArguments.Parse(
            recipient,
            seed,
            count,
            until,
            days,
            attachmentBytes,
            sensitivePercentage,
            intervalMilliseconds,
            configurationPath: null,
            dryRun: false,
            new FakeTimeProvider(Today));
}
