// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>One persisted passage that has no vector under the profile being embedded into.</summary>
/// <remarks>
/// The passage text is mail content. It exists on this record because a provider has to be sent it, and it may reach
/// nothing else: no log, no metric, no trace, and no failure message.
/// </remarks>
/// <param name="Id">The passage the vector will hang on.</param>
/// <param name="Text">The passage itself, as the chunker cut it.</param>
public sealed record EmailChunkAwaitingEmbedding(EmailChunkId Id, string Text);
