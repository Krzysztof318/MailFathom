// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Settings;

/// <summary>
/// Covers what the commit refuses before it reaches the database. Whether the statement replaces the row is proved
/// against a real server in the integration suite; what belongs here is the bound the write shares with the read,
/// because a document permitted past it would persist a row the next start refuses.
/// </summary>
public sealed class RootSettingsDocumentWriterTests
{
    private static readonly DateTimeOffset AnyInstant = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A document past what the layer composes settings from is refused rather than persisted.</summary>
    [Fact]
    public async Task CommitAsync_ADocumentPastWhatTheLayerComposesFrom_IsRefused()
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=mailfathom");
        var writer = new RootSettingsDocumentWriter(dataSource, new FakeTimeProvider(AnyInstant));
        var oversized = $$"""{ "Padding": "{{new string('x', RootSettingsDocument.MaximumOctets)}}" }""";

        // Act & Assert
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.CommitAsync(oversized, expectedVersion: 1, TestContext.Current.CancellationToken));
        Assert.Contains("octets", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A candidate that is not a document at all is a caller's mistake rather than a statement to attempt.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CommitAsync_ACandidateThatIsNotADocument_IsRefused(string json)
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=mailfathom");
        var writer = new RootSettingsDocumentWriter(dataSource, new FakeTimeProvider(AnyInstant));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.CommitAsync(json, expectedVersion: 1, TestContext.Current.CancellationToken));
    }

    /// <summary>No document ever stood at a negative version, so a commit against one is refused.</summary>
    [Fact]
    public async Task CommitAsync_ANegativeExpectedVersion_IsRefused()
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=mailfathom");
        var writer = new RootSettingsDocumentWriter(dataSource, new FakeTimeProvider(AnyInstant));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            writer.CommitAsync("{}", expectedVersion: -1, TestContext.Current.CancellationToken));
    }
}
