// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using System.Threading.Channels;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Common.Observability;
using MailFathom.Domain.Emails;

namespace MailFathom.Infrastructure.Embeddings;

/// <summary>An in-process bounded backlog of messages waiting to be embedded, and the instruments that make it visible.</summary>
/// <remarks>
/// <para>
/// In-process rather than durable, and that is the decision rather than a limitation to fix later. Losing the backlog at
/// shutdown loses no work: what it holds are identifiers of messages already committed with their passages, so the
/// backfill finds every one of them by asking the database which passages lack a vector. A durable backlog would be a
/// second answer to a question the schema already answers, and the two could disagree.
/// </para>
/// <para>
/// The bound refuses an offer rather than making the producer wait, because the producer is a synchronization run
/// holding an open IMAP session. A refusal is counted and the depth is published, so an instance falling behind is
/// visible rather than invisible.
/// </para>
/// </remarks>
internal sealed class BoundedEmailEmbeddingBacklog : IEmailEmbeddingBacklog
{
    private readonly Channel<StoredEmailId> waiting;
    private readonly Counter<long> refusedOfferCount;

    /// <summary>Initializes a backlog bounded by what the deployment configured.</summary>
    /// <param name="options">The bound on how many messages may wait at once.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured capacity is not positive.</exception>
    public BoundedEmailEmbeddingBacklog(EmailEmbeddingBacklogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity, nameof(options));

        // A single reader because one worker consumes the backlog, and many writers because every account's
        // synchronization run offers into it. The full mode never takes effect: nothing here ever waits to write.
        this.waiting = Channel.CreateBounded<StoredEmailId>(new BoundedChannelOptions(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        this.refusedOfferCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.embedding.backlog.refused",
            unit: "{message}",
            description: "Messages the embedding backlog refused because it was already holding its bound.");

        // Not held in a field: the meter keeps every instrument published on it alive, and the callback keeps this
        // backlog alive with it, so a gauge nobody references still reports.
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.embedding.backlog.depth",
            () => this.Depth,
            unit: "{message}",
            description: "Messages waiting to be embedded.");
    }

    /// <inheritdoc />
    public int Depth => this.waiting.Reader.Count;

    /// <inheritdoc />
    public bool TryEnqueue(StoredEmailId storedEmailId)
    {
        if (this.waiting.Writer.TryWrite(storedEmailId))
        {
            return true;
        }

        this.refusedOfferCount.Add(1);

        return false;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StoredEmailId> ReadAllAsync(CancellationToken cancellationToken) =>
        this.waiting.Reader.ReadAllAsync(cancellationToken);
}
