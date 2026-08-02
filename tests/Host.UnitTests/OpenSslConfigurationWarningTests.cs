// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what an operator is told when the process takes its TLS parameters from a configured OpenSSL file.</summary>
/// <remarks>
/// The message is the only place a relaxed TLS policy is visible from inside MailFathom: nothing about it reaches a
/// settings file, and OpenSSL read it before any configuration existed. Its content is therefore a contract — it has to
/// name the path and say that the scope is the whole process — and so is its silence, because a warning that fired for
/// a deployment running the platform default would be one nobody reads.
/// </remarks>
public sealed class OpenSslConfigurationWarningTests
{
    [Fact]
    public async Task StartAsync_ConfiguredOpenSslFile_WarnsThatEveryConnectionInTheProcessIsGovernedByIt()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor("/etc/mailfathom/openssl-legacy.cnf", logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("weaker than that default", record.Message, StringComparison.Ordinal);
        Assert.Contains("the whole process", record.Message, StringComparison.Ordinal);
        Assert.Equal(
            "/etc/mailfathom/openssl-legacy.cnf",
            Assert.Contains("OpenSslConfigurationPath", record.Properties));
    }

    /// <summary>The posture a deployment that configured nothing has, which is the platform's own and needs no report.</summary>
    [Fact]
    public async Task StartAsync_NoConfiguredOpenSslFile_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(configurationFilePath: null, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>
    /// A variable exported without a value is what a half-written shell profile or a container manifest with an empty
    /// entry produces. OpenSSL reads it as no configuration file at all, so reporting a weakened process would name a
    /// posture that is not in force.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartAsync_BlankOpenSslFilePath_SaysNothing(string configurationFilePath)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(configurationFilePath, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task StopAsync_ConfiguredOpenSslFile_CompletesWithoutSayingAnythingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor("/etc/mailfathom/openssl-legacy.cnf", logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static OpenSslConfigurationWarning WarningFor(string? configurationFilePath, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new OpenSslConfigurationWarning(
            configurationFilePath,
            loggerFactory.CreateLogger<OpenSslConfigurationWarning>());
    }
}
