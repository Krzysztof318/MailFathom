// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Observability;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the span a local read is published as, keeping what each read reported.</summary>
/// <remarks>
/// It records the operation, what the read said it returned, and that the scope was closed, which is the whole of what a
/// use case decides. What those become — a span name, a tag, an ending — is the adapter's contract and is proved against
/// a real listener where that adapter lives.
/// </remarks>
internal sealed class RecordingMailboxReadTelemetry : IMailboxReadTelemetry
{
    private readonly List<PublishedRead> reads = [];
    private readonly List<PublishedRead> rankings = [];

    /// <summary>Gets the reads that were opened, in the order they began.</summary>
    public IReadOnlyList<PublishedRead> Reads => this.reads;

    /// <summary>Gets the rankings that were opened, in the order they began.</summary>
    /// <remarks>
    /// Kept apart from the reads because a ranking happens inside one: a search that opened a read and no ranking is a
    /// different defect from one that opened neither.
    /// </remarks>
    public IReadOnlyList<PublishedRead> Rankings => this.rankings;

    /// <inheritdoc />
    public IMailboxReadScope BeginRead(MailboxReadOperation operation, CancellationToken cancellationToken)
    {
        var read = new PublishedRead(operation);
        this.reads.Add(read);

        return read;
    }

    /// <inheritdoc />
    public IMailboxReadScope BeginSearchRanking(CancellationToken cancellationToken)
    {
        var ranking = new PublishedRead(MailboxReadOperation.SearchMailbox);
        this.rankings.Add(ranking);

        return ranking;
    }

    /// <summary>One opened read and what it reported before its scope was closed.</summary>
    internal sealed class PublishedRead(MailboxReadOperation operation) : IMailboxReadScope
    {
        /// <summary>Gets the read that was opened.</summary>
        public MailboxReadOperation Operation => operation;

        /// <summary>Gets what the read said it returned, or <see langword="null" /> while it has reported nothing.</summary>
        public int? ResultCount { get; private set; }

        /// <summary>Gets whether the scope was closed, which a read conducted inside it always is.</summary>
        public bool WasClosed { get; private set; }

        /// <inheritdoc />
        public void Completed(int resultCount) => this.ResultCount = resultCount;

        /// <inheritdoc />
        public void Dispose() => this.WasClosed = true;
    }
}
