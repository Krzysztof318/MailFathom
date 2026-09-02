// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.CompilerServices;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Serves a fixed set of messages and then ends, so a worker's loop finishes instead of waiting.</summary>
/// <remarks>
/// Hand-written rather than substituted because what a worker test needs is a sequence that completes on its own: an
/// asynchronous stream configured through a substitute would still have to be written by hand, and this states the end
/// of the sequence where a reader of the test can see it.
/// </remarks>
internal sealed class ScriptedEmailEmbeddingBacklog : IEmailEmbeddingBacklog
{
    private readonly Queue<StoredEmailId> waiting;

    /// <summary>Initializes a backlog that serves these messages once, in order.</summary>
    public ScriptedEmailEmbeddingBacklog(params IReadOnlyList<StoredEmailId> messages) => this.waiting = new(messages);

    /// <inheritdoc />
    public int Depth => this.waiting.Count;

    /// <inheritdoc />
    public bool TryEnqueue(StoredEmailId storedEmailId)
    {
        this.waiting.Enqueue(storedEmailId);

        return true;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StoredEmailId> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (this.waiting.TryDequeue(out var storedEmailId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return storedEmailId;
        }

        await Task.CompletedTask;
    }
}
