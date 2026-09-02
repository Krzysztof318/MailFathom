// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>One profile row, with the geometry it fixed when it was registered.</summary>
/// <remarks>
/// <para>
/// Lifecycle-neutral by design, because a profile is a generation and two of them coexist while a new one is built: the
/// one retrieval reads and the one being embedded into are the same shape and are told apart by
/// <see cref="EmbeddingProfileLifecycleState" /> rather than by their type. Its absence is what "this instance does not
/// embed" means — a profile row exists, or it does not, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// records why the switch is an activation rather than a setting.
/// </para>
/// <para>
/// The identity travels with the identifier rather than being looked up beside it, because a caller about to spend on a
/// provider call needs both: the identifier says which rows the vectors hang on, and the identity says which space they
/// have to belong to for that attribution to be true.
/// </para>
/// </remarks>
/// <param name="Id">Which registered profile this is.</param>
/// <param name="Identity">The geometry the profile fixed at registration.</param>
public sealed record RegisteredEmbeddingProfile(EmbeddingProfileId Id, EmbeddingProfileIdentity Identity);
