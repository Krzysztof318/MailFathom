// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Providers;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup which declared AI endpoints this deployment reaches over a hop nothing encrypts.</summary>
/// <remarks>
/// <para>
/// Reaching a model server over a plain address is a supported posture rather than a mistake, which is why this reports
/// and does not refuse: it is the ordinary shape of a server the operator runs themselves, and refusing it would mean
/// refusing the only arrangement in which no mail content leaves the operator's own machines at all. What startup does
/// refuse is the combination that is unambiguously wrong — a credential travelling on such a hop —
/// <see cref="ProviderEndpointReachRules" /> holds that rule.
/// </para>
/// <para>
/// The part no rule can decide is what the hop itself costs, so it is reported instead. Nothing readable from the
/// configuration says whether a host name belongs to a container beside this process or to a service across the
/// internet, and the mail that crosses the hop is confidential either way: the passages sent to be embedded, the
/// question asked of a chat model, the passages it is given to answer it, and the answer it returns. Only the operator
/// knows which network that is, so the report tells them what is on the wire and leaves the judgement with them —
/// which is how <see cref="McpTransportEncryptionWarning" /> treats the same question at the other end of the process.
/// </para>
/// <para>
/// A startup report, like every warning beside it. The chat declaration reloads, so an address edited into it later is
/// adopted without passing through here; the embedding chain takes a restart and cannot move at all.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class AiProviderTransportEncryptionWarning : IHostedService
{
    private readonly EmbeddingOptions embeddingSettings;
    private readonly ISettingsSnapshot<ChatModelOptions> chatSettings;
    private readonly ILogger<AiProviderTransportEncryptionWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="embeddingSettings">The embedding chain startup was composed from.</param>
    /// <param name="chatSettings">The published chat declaration, which is the one in force when this runs.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="embeddingSettings" /> or <paramref name="chatSettings" /> is <see langword="null" />.</exception>
    public AiProviderTransportEncryptionWarning(
        IOptions<EmbeddingOptions> embeddingSettings,
        ISettingsSnapshot<ChatModelOptions> chatSettings,
        ILogger<AiProviderTransportEncryptionWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingSettings);
        ArgumentNullException.ThrowIfNull(chatSettings);

        this.embeddingSettings = embeddingSettings.Value;
        this.chatSettings = chatSettings;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var clearTextEmbeddingAliases = this.embeddingSettings.Endpoints
            .Where(endpoint => ProviderEndpointReachRules.IsReachedInClearText(endpoint.Address))
            .Select(endpoint => endpoint.Alias.Trim());

        foreach (var endpointAlias in clearTextEmbeddingAliases)
        {
            this.LogEmbeddingEndpointReachedInClearText(endpointAlias);
        }

        var chat = this.chatSettings.Current;

        if (chat.IsConfigured && ProviderEndpointReachRules.IsReachedInClearText(chat.Address))
        {
            this.LogChatEndpointReachedInClearText(chat.Alias.Trim());
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Embedding endpoint {EndpointAlias} is declared with a plain http address, so every passage of your "
            + "mail that is embedded crosses that hop readable by anything on the network path. No credential is on it, "
            + "because startup refuses one over a plain address, but the mail content is not protected either. This is "
            + "the expected posture for a model server you run yourself on a network you control; anywhere else, give "
            + "the endpoint an https address.")]
    private partial void LogEmbeddingEndpointReachedInClearText(string endpointAlias);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Chat endpoint {EndpointAlias} is declared with a plain http address, so the question asked, the "
            + "passages of your mail the model is given to answer it, and the answer it returns all cross that hop "
            + "readable by anything on the network path. No credential is on it, because startup refuses one over a "
            + "plain address, but nothing else on the hop is protected either. This is the expected posture for a model "
            + "server you run yourself on a network you control; anywhere else, give the endpoint an https address.")]
    private partial void LogChatEndpointReachedInClearText(string endpointAlias);
}
