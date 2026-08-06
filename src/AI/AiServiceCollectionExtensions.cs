// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.AI;

/// <summary>Registers what the AI boundary implements for the rest of the application.</summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>Registers the derivations retrieval is built on that reach no provider and no network.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Separate from whatever registers a provider adapter, because an instance with no embedding provider configured
    /// still chunks the mail it synchronizes: the chunks are what a later activation embeds, and deriving them costs
    /// nothing an operator has to consent to. The chunker is a singleton because it holds no state at all.
    /// </remarks>
    public static IServiceCollection AddLocalTextDerivations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(EmailChunkingRules.Current);
        services.AddSingleton<IEmailTextChunker, DeterministicEmailTextChunker>();

        return services;
    }
}
