// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chat;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Publishes the chat plan built from the declaration currently in force.</summary>
/// <remarks>
/// <para>
/// The mapping is the same one composition used; what this adds is when it runs. Mapping once at registration froze the
/// plan for the life of the process, so every key under <c>Chat</c> cost a restart of a host that is synchronizing
/// mailboxes and holding an IMAP IDLE connection — and picking a model is exactly the setting an operator iterates on,
/// because a model the provider refuses is only discovered from a refusal.
/// </para>
/// <para>
/// The plan is cached against the settings instance it was mapped from, so a published declaration has one plan and
/// reading it costs no allocation until a reload lands. Two threads reaching an uncached instance both map it and one
/// wins the write: the two plans describe the same declaration, so which one wins decides nothing.
/// </para>
/// <para>
/// Only the plan itself is republished. A run reads it once from its own scope and holds it for the run, so a
/// reload landing mid-question changes the next question rather than that one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this plan source.")]
internal sealed class ChatGenerationPlanSource(ISettingsSnapshot<ChatModelOptions> publishedSettings)
    : IChatGenerationPlanSource
{
    private MappedPlan? mapped;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the published declaration carries no chat endpoint, which composition registers this only against.</exception>
    public ChatGenerationPlan Current
    {
        get
        {
            var settings = publishedSettings.Current;

            if (Volatile.Read(ref this.mapped) is { } existing && ReferenceEquals(existing.Settings, settings))
            {
                return existing.Plan;
            }

            // Reached only where the endpoint was declared at registration, and a reload that would remove it is
            // refused before it is published, so the absence is a contradiction rather than a configuration state.
            var plan = ChatGenerationPlanMapper.Map(settings)
                ?? throw new InvalidOperationException(
                    "The chat endpoint was declared at registration and is absent from the configuration in force.");

            Volatile.Write(ref this.mapped, new MappedPlan(settings, plan));

            return plan;
        }
    }

    /// <summary>The plan a published declaration maps to, kept beside the declaration it came from.</summary>
    /// <param name="Settings">The published declaration, held to recognize the next one by reference.</param>
    /// <param name="Plan">What that declaration maps to.</param>
    private sealed record MappedPlan(ChatModelOptions Settings, ChatGenerationPlan Plan);
}
