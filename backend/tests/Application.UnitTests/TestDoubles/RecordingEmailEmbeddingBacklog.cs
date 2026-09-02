// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.CompilerServices;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps the messages a run offered for embedding, and can be told to refuse them.</summary>
/// <remarks>
/// Hand-written rather than substituted because the tests using it assert both on what was offered and on what a
/// refusal changed about the run, and a recorded list reports the first without a matcher while a settable bound
/// expresses the second without configuring a call.
/// </remarks>
internal sealed class RecordingEmailEmbeddingBacklog : IEmailEmbeddingBacklog
{
    private readonly List<StoredEmailId> offered = [];

    /// <summary>Gets or sets how many offers are accepted before the rest are refused.</summary>
    public int Capacity { get; set; } = int.MaxValue;

    /// <summary>Gets the messages that were accepted, in the order they were offered.</summary>
    public IReadOnlyList<StoredEmailId> Accepted => this.offered;

    /// <summary>Gets how many offers the bound refused.</summary>
    public int RefusedCount { get; private set; }

    /// <inheritdoc />
    public int Depth => this.offered.Count;

    /// <inheritdoc />
    public bool TryEnqueue(StoredEmailId storedEmailId)
    {
        if (this.offered.Count >= this.Capacity)
        {
            this.RefusedCount++;

            return false;
        }

        this.offered.Add(storedEmailId);

        return true;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StoredEmailId> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var storedEmailId in this.offered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return storedEmailId;
        }

        await Task.CompletedTask;
    }
}
