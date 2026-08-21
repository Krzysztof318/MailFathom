// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using BenchmarkDotNet.Attributes;
using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.TestSupport;

namespace MailFathom.Benchmarks;

/// <summary>How long cutting extracted text into passages takes, and what it allocates.</summary>
/// <remarks>
/// The path a whole-mailbox re-cut is made of. Moving the boundary rules re-cuts every stored message, so what this
/// number decides is how long a rule change takes to reach the mailbox it applies to.
/// </remarks>
public class ChunkingBenchmarks
{
    private readonly DeterministicEmailTextChunker chunker = new();

    private ExtractedEmailText text = null!;

    /// <summary>Composes the text every iteration cuts, outside what is measured.</summary>
    [GlobalSetup]
    public void ComposeText() => this.text = LargeSyntheticMessage.AsExtractedText();

    /// <summary>Cuts a long extracted text into overlapping passages.</summary>
    /// <returns>The passages, returned so nothing about the cut can be optimized away.</returns>
    [Benchmark]
    public EmailChunkingResult DeriveChunks() =>
        this.chunker.DeriveChunks(this.text, EmailChunkingRules.Current, EmbeddingInputBound.Default);
}
