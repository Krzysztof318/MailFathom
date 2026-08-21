// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>One passage paired with where a generator placed it in the active profile's space.</summary>
/// <remarks>
/// The passage text is deliberately absent: the vector has been produced, so nothing downstream of generation needs the
/// words again, and carrying them to the write would keep mail content alive for the length of a transaction that has
/// no use for it.
/// </remarks>
/// <param name="ChunkId">The passage the vector stands for.</param>
/// <param name="Vector">The point the passage was placed at.</param>
public sealed record GeneratedChunkEmbedding(EmailChunkId ChunkId, EmbeddingVector Vector);
