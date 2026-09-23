// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Chat;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup which declared chat models ask for sticky sessions from a provider that does not honour them.</summary>
/// <remarks>
/// <para>
/// A report rather than a refusal. OpenRouter is the one provider known to route by a session today, so a model reached
/// anywhere else sends none — and a configuration carrying the setting while it moves between providers is still a
/// working configuration, only one where the setting does nothing. The operator is told that, once, by alias.
/// </para>
/// <para>
/// A startup report, like every warning beside it. The chat declaration reloads, so a model moved off OpenRouter later
/// stops sending sessions without passing through here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ChatStickySessionsIgnoredWarning : IHostedService
{
    private readonly ISettingsSnapshot<ChatModelOptions> chatSettings;
    private readonly ILogger<ChatStickySessionsIgnoredWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="chatSettings">The published chat declaration, which is the one in force when this runs.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="chatSettings" /> is <see langword="null" />.</exception>
    public ChatStickySessionsIgnoredWarning(
        ISettingsSnapshot<ChatModelOptions> chatSettings,
        ILogger<ChatStickySessionsIgnoredWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(chatSettings);

        this.chatSettings = chatSettings;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var ignoringAliases = this.chatSettings.Current.Models
            .Select(static model => model.ToEndpoint())
            .Where(static endpoint => endpoint.StickySessions && !endpoint.SendsStickySessions)
            .Select(static endpoint => endpoint.Alias);

        foreach (var modelAlias in ignoringAliases)
        {
            this.LogStickySessionsIgnored(modelAlias);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Chat model {ModelAlias} declares StickySessions, but only OpenRouter honours a sticky session today "
            + "and this model is reached elsewhere, so its requests carry none and the setting is ignored.")]
    private partial void LogStickySessionsIgnored(string modelAlias);
}
