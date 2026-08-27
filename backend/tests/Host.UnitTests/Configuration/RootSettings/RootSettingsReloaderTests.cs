// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Settings;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

/// <summary>
/// Covers what a reload does to the version in force. A failed reload is the case the contract is written for: the
/// deployment keeps serving the settings it had, and the operator is told which version did not take.
/// </summary>
public sealed class RootSettingsReloaderTests
{
    private const string InForce = """{ "Layered": { "Setting": "inForce" } }""";

    /// <summary>A usable candidate is published, and the version it carried becomes the version in force.</summary>
    [Fact]
    public async Task ReloadAsync_UsableCandidate_PublishesIt()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = ReaderReturning(new RootSettingsDocument("""{ "Layered": { "Setting": "reloaded" } }""", Version: 7));
        var logger = new RecordingLogger<RootSettingsReloader>();

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(published);
        Assert.Equal(7, provider.Version);
        provider.TryGet("Layered:Setting", out var effective);
        Assert.Equal("reloaded", effective);
    }

    /// <summary>
    /// A candidate that is not a configuration document is rejected by version, and the version already in force stays
    /// in force rather than the layer emptying onto the files beneath it.
    /// </summary>
    [Fact]
    public async Task ReloadAsync_CandidateThatIsNotAConfigurationDocument_KeepsTheVersionInForceAndReportsTheRejectedOne()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = ReaderReturning(new RootSettingsDocument("\"not settings\"", Version: 9));
        var logger = new RecordingLogger<RootSettingsReloader>();

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.Equal(3, provider.Version);
        provider.TryGet("Layered:Setting", out var effective);
        Assert.Equal("inForce", effective);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("version 9", StringComparison.Ordinal)
                && message.Contains("Version 3", StringComparison.Ordinal));
    }

    /// <summary>
    /// A candidate nested deeper than the JSON reader accepts reaches the parser as a different exception type than a
    /// document of the wrong shape does, and <c>jsonb</c> stores one happily. It is the same outcome for the
    /// deployment, so it is the same outcome here rather than an exception escaping the reload.
    /// </summary>
    [Fact]
    public async Task ReloadAsync_CandidateNestedDeeperThanTheReaderAccepts_KeepsTheVersionInForce()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = ReaderReturning(new RootSettingsDocument(NestedDocument(depth: 96), Version: 11));
        var logger = new RecordingLogger<RootSettingsReloader>();

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.Equal(3, provider.Version);
        provider.TryGet("Layered:Setting", out var effective);
        Assert.Equal("inForce", effective);
        Assert.Contains(logger.Messages, message => message.Contains("version 11", StringComparison.Ordinal));
    }

    /// <summary>
    /// A candidate carrying a setting the layer was reached through is rejected like any other unusable document, so a
    /// write cannot republish the terms the layer itself is trusted under.
    /// </summary>
    [Fact]
    public async Task ReloadAsync_CandidateCarryingABootstrapSetting_KeepsTheVersionInForce()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = ReaderReturning(
            new RootSettingsDocument("""{ "Secrets": { "Interpretation": "PlaintextAllowed" } }""", Version: 13));
        var logger = new RecordingLogger<RootSettingsReloader>();

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.Equal(3, provider.Version);
        provider.TryGet("Secrets:Interpretation", out var interpretation);
        Assert.Null(interpretation);
        provider.TryGet("Layered:Setting", out var effective);
        Assert.Equal("inForce", effective);
        Assert.Contains(logger.Messages, message => message.Contains("version 13", StringComparison.Ordinal));
    }

    /// <summary>
    /// A candidate carrying a setting the storage catalog persists elsewhere is rejected the same way, because a reload
    /// that published it would leave one setting readable from two stores.
    /// </summary>
    [Fact]
    public async Task ReloadAsync_CandidateCarryingASpeciallyRoutedSetting_KeepsTheVersionInForce()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = ReaderReturning(
            new RootSettingsDocument("""{ "Accounts": { "0": { "DisplayName": "owner" } } }""", Version: 17));
        var logger = new RecordingLogger<RootSettingsReloader>();

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.Equal(3, provider.Version);
        provider.TryGet("Accounts:0:DisplayName", out var owner);
        Assert.Null(owner);
        Assert.Contains(logger.Messages, message => message.Contains("version 17", StringComparison.Ordinal));
    }

    /// <summary>A database that cannot be read leaves the deployment exactly as it was, and says so.</summary>
    [Fact]
    public async Task ReloadAsync_PersistedConfigurationUnreadable_KeepsTheVersionInForce()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = Substitute.For<IRootSettingsDocumentReader>();
        var logger = new RecordingLogger<RootSettingsReloader>();

        reader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<Task<RootSettingsDocument>>(_ => throw new RootSettingsUnreadableException("unreachable"));

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.Equal(3, provider.Version);
        provider.TryGet("Layered:Setting", out var effective);
        Assert.Equal("inForce", effective);
        Assert.Contains(logger.Messages, message => message.Contains("could not be re-read", StringComparison.Ordinal));
    }

    /// <summary>
    /// A reload racing a newer one loses, and losing is an ordinary outcome rather than a failure: the version it read
    /// was already superseded by the time it came to publish, so the newer document stays in force and the reload
    /// reports that it published nothing. The record of it is written at information rather than at warning, because
    /// nothing about the deployment is wrong.
    /// </summary>
    [Fact]
    public async Task ReloadAsync_CandidateSupersededBeforeItWasPublished_KeepsTheNewerVersionInForce()
    {
        // Arrange
        var provider = LoadedProvider();
        var reader = ReaderReturning(new RootSettingsDocument("""{ "Layered": { "Setting": "superseded" } }""", Version: 2));
        var logger = new RecordingLogger<RootSettingsReloader>();

        // Act
        var published = await new RootSettingsReloader(provider, reader, logger).ReloadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.Equal(3, provider.Version);
        provider.TryGet("Layered:Setting", out var effective);
        Assert.Equal("inForce", effective);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("version 2", StringComparison.Ordinal)
                && message.Contains("version 3", StringComparison.Ordinal));
    }

    /// <summary>Composes an object nested to the given depth, which PostgreSQL accepts and the JSON reader stops at.</summary>
    private static string NestedDocument(int depth) =>
        string.Concat(Enumerable.Repeat("""{ "Nested": """, depth))
        + "\"leaf\""
        + new string('}', depth);

    private static IRootSettingsDocumentReader ReaderReturning(RootSettingsDocument document)
    {
        var reader = Substitute.For<IRootSettingsDocumentReader>();

        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(document);

        return reader;
    }

    private static RootSettingsConfigurationProvider LoadedProvider()
    {
        var provider = new RootSettingsConfigurationProvider(new RootSettingsDocument(InForce, Version: 3));

        provider.Load();

        return provider;
    }
}
