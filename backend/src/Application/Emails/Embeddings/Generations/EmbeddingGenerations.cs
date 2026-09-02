// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>The generations this instance holds: the one retrieval reads, and the one being built beside it.</summary>
/// <remarks>
/// <para>
/// Two rather than one, because changing model must not take semantic search away for as long as a mailbox takes to
/// re-embed. The generation being built is never read, and the one serving stays authoritative until the new one is
/// complete — which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// means by making the profile the generation: there is no counter beside these two rows.
/// </para>
/// <para>
/// Both are absent on an instance that has activated nothing, which is a supported deployment rather than a fault.
/// </para>
/// <para>
/// A generation is the versioned epoch of the index and never the act of filling one: turning a stored message's
/// passages into vectors belongs to <c>Embeddings.Vectorization</c>, which writes into whichever of these two
/// <see cref="Target" /> names.
/// </para>
/// </remarks>
/// <param name="Serving">The generation retrieval reads and newly synchronized mail is embedded into, or <see langword="null" /> when this instance has activated none.</param>
/// <param name="Building">The generation a reindex is filling, or <see langword="null" /> when no reindex is running.</param>
public sealed record EmbeddingGenerations(
    RegisteredEmbeddingProfile? Serving,
    RegisteredEmbeddingProfile? Building)
{
    /// <summary>An instance that has registered no profile at all.</summary>
    public static EmbeddingGenerations None { get; } = new(Serving: null, Building: null);

    /// <summary>Gets the generation the sweep works towards, which is the one being built where there is one.</summary>
    /// <remarks>
    /// The sweep fills the new generation in preference to the old, because the old one is complete by the time a new
    /// one exists and every passage the sweep pays for is one the switch will not have to wait for. Mail arriving while
    /// it runs is embedded into <see cref="Serving" /> by the live path instead, so it stays searchable throughout; the
    /// sweep reaches it for the new generation before the count that decides the switch can read zero.
    /// </remarks>
    public RegisteredEmbeddingProfile? Target => this.Building ?? this.Serving;
}
