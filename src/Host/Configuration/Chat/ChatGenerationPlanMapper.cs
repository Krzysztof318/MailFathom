// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Turns the bound chat declaration into the plan the provider adapter runs on.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and full of empty strings that mean absence, while the plan is the validated value the
/// adapter is allowed to assume. Keeping the two apart is what lets the adapter hold no defaulting logic at all.
/// </remarks>
internal static class ChatGenerationPlanMapper
{
    /// <summary>Builds the plan a declared chat endpoint describes.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <returns>The plan, or <see langword="null" /> when the deployment declared no chat provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing declared is not a failure. An instance that has not chosen a chat provider serves every read path exactly
    /// as it did before, and returning nothing is what lets the composition root register no client rather than one that
    /// fails at first use.
    /// </remarks>
    public static ChatGenerationPlan? Map(ChatModelOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.IsConfigured
            ? ChatGenerationPlan.Create(
                settings.ToEndpoint(),
                settings.MaxOutputTokens,
                settings.Temperature,
                settings.TopP,
                settings.MaxMessagesPerRequest,
                settings.MaxRequestCharacters,
                settings.RequestTimeout)
            : null;
    }
}
