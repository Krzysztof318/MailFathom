// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>Holds the one rule the two AI declarations share: an alias names one endpoint across the whole deployment.</summary>
/// <remarks>
/// <para>
/// Neither section can enforce this on its own, because each validates what it declares and neither can see the other.
/// It lives here rather than inline in the composition root so it is reachable from a test: it is the rule that keeps a
/// chat outage from opening the circuit the embeddings are served through, and a comparison written the wrong way round
/// — or one that quietly loses its case-insensitivity — would leave that true while looking correct.
/// </para>
/// <para>
/// The check is one-directional by construction. Duplicate aliases *within* the embedding chain are refused by
/// <see cref="EmbeddingOptions" /> itself and duplicates within the declared chat models by
/// <see cref="ChatModelOptions" />, so what is left is exactly whether a chat alias reappears in the chain.
/// </para>
/// </remarks>
internal static class ProviderEndpointAliases
{
    /// <summary>Finds the embedding endpoint alias a declared chat model reuses, if any.</summary>
    /// <param name="embeddings">The bound embedding declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="chat">The bound chat declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <returns>The reused alias as the embedding chain spells it, or <see langword="null" /> when nothing collides.</returns>
    /// <remarks>
    /// Compared without case, and after trimming both sides, because that is how the alias is matched everywhere else it
    /// is used — a credential resolved by it would otherwise reach one endpoint while a log line named the other. Every
    /// declared chat model is read rather than only the one that answers questions, because each is an endpoint of its
    /// own with its own credential and its own circuit.
    /// </remarks>
    public static string? FindReusedAlias(EmbeddingOptions? embeddings, ChatModelOptions? chat)
    {
        if (chat is null || embeddings is null)
        {
            return null;
        }

        var chatAliases = chat.DeclaredAliases;

        return embeddings.Endpoints
            .Select(endpoint => endpoint.Alias.Trim())
            .FirstOrDefault(alias => chatAliases.Contains(alias, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Describes the collision for an operator, naming the alias and what it costs to leave it.</summary>
    /// <param name="reusedAlias">The alias both sections declare.</param>
    /// <returns>The message a startup failure carries.</returns>
    public static string DescribeReusedAlias(string reusedAlias) =>
        $"A chat model and an embedding endpoint both declare the alias '{reusedAlias}'. An alias names one "
        + "endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.";
}
