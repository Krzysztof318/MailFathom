// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about a model declaring sticky sessions where no provider honours them.</summary>
public sealed class ChatStickySessionsIgnoredWarningTests
{
    [Fact]
    public async Task StartAsync_AModelDeclaringStickySessionsAwayFromOpenRouter_SaysTheSettingIsIgnored()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(Sticky("answering", "https://provider.invalid/v1/"), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("only OpenRouter honours", record.Message, StringComparison.Ordinal);
        Assert.Equal("answering", Assert.Contains("ModelAlias", record.Properties));
        Assert.DoesNotContain("provider.invalid", record.Message, StringComparison.Ordinal);
    }

    /// <summary>A model on OpenRouter honours the setting, and one not declaring it has nothing to ignore.</summary>
    [Theory]
    [InlineData("https://openrouter.ai/api/v1/", true)]
    [InlineData("https://provider.invalid/v1/", false)]
    public async Task StartAsync_AModelWhoseSettingIsNotIgnored_SaysNothing(string address, bool stickySessions)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var model = DeclaredChatModels.Model(address: address);
        model.StickySessions = stickySessions;
        var warning = WarningOver(model, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static ChatModelDeclarationOptions Sticky(string alias, string address)
    {
        var model = DeclaredChatModels.Model(alias, address: address);
        model.StickySessions = true;

        return model;
    }

    private static ChatStickySessionsIgnoredWarning WarningOver(ChatModelDeclarationOptions model, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new ChatStickySessionsIgnoredWarning(
            new StubSettingsSnapshot<ChatModelOptions>(DeclaredChatModels.Section(model)),
            loggerFactory.CreateLogger<ChatStickySessionsIgnoredWarning>());
    }
}
