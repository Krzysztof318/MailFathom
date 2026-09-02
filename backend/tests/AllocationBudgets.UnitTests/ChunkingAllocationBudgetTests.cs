// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AllocationBudgets.UnitTests;

/// <summary>What cutting one message's extracted text into passages may allocate.</summary>
/// <remarks>
/// Chunking runs over every message that reaches the derived pipeline, and it runs again over the whole mailbox
/// whenever the boundary rules move, so this is the path a re-cut of twenty thousand messages is made of. Unlike the
/// three reading paths, it allocates its result by construction — the passages are what it produces — so the budget is
/// a multiple of the source rather than a fraction of it.
/// </remarks>
public sealed class ChunkingAllocationBudgetTests
{
    /// <summary>What one cut may allocate, as a multiple of the text it walks.</summary>
    /// <remarks>
    /// The passages are the result and cost the source once over, plus the overlap two neighbouring chunks share, plus
    /// the UTF-8 encoding and hexadecimal digest each passage's content hash is computed through. Eight times the
    /// source is what that comes to with room to spare, and it is the bound that matters rather than the number: a walk
    /// that re-materialized the remaining text at every window would allocate a multiple of the window count, which on
    /// text this long is two orders of magnitude above this.
    /// </remarks>
    private const int MaximumAllocatedBytesPerSourceByte = 8;

    /// <summary>Bytes one character of the source occupies while the walk holds it.</summary>
    private const int BytesPerCharacter = sizeof(char);

    /// <summary>Cutting a long extracted text costs the passages it produces and not a multiple of them.</summary>
    [Fact]
    public async Task DeriveChunks_LongText_StaysWithinItsAllocationBudget()
    {
        // Arrange
        var chunker = new DeterministicEmailTextChunker();
        var text = LargeSyntheticMessage.AsExtractedText();
        var sourceBytes = (long)text.TrimmedText!.Length * BytesPerCharacter;
        var budgetBytes = sourceBytes * MaximumAllocatedBytesPerSourceByte;

        // Establishing that the run really cuts the text into many passages is a step of its own, so the measured run
        // can assert nothing: a budget met by producing one chunk would say nothing about the walk.
        var chunking = chunker.DeriveChunks(text, EmailChunkingRules.Current, EmbeddingInputBound.Default);
        Assert.True(
            chunking.Chunks.Count > 10,
            $"The measured text was cut into {chunking.Chunks.Count} passages, too few for a budget over the walk to mean anything.");

        // Act, Assert
        await AllocationBudget.AssertWithinAsync(
            "Cutting a long extracted text into passages",
            budgetBytes,
            () =>
            {
                _ = chunker.DeriveChunks(text, EmailChunkingRules.Current, EmbeddingInputBound.Default);

                return Task.CompletedTask;
            });
    }
}
