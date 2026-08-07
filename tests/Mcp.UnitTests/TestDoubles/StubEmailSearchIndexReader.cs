// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Answers a search with a fixed window and records what the use case asked for.</summary>
/// <remarks>
/// The window is returned exactly as it was given, whatever limit and whatever bounds the read was issued with. That is
/// deliberate: it is how a test observes the bounds this boundary applies to what it publishes, which would otherwise be
/// invisible behind an adapter that had already applied them. The ranking half answers from the same window, so the two
/// halves of the port cannot disagree about which emails a query found.
/// </remarks>
internal sealed class StubEmailSearchIndexReader(params EmailSearchMatch[] window) : IEmailSearchIndexReader
{
    /// <summary>Gets the selection the last read was issued with, or <see langword="null" /> when nothing was read.</summary>
    public MailboxEmailSelection? LastSelection { get; private set; }

    /// <summary>Gets the query text the last read was issued with.</summary>
    public EmailSearchQueryText? LastQueryText { get; private set; }

    /// <summary>Gets the snippet bounds the last window read was issued with.</summary>
    public EmailSearchSnippetBounds? LastSnippetBounds { get; private set; }

    /// <summary>Gets the result count the last ranking asked for.</summary>
    public int LastLimit { get; private set; }

    /// <summary>Gets how many rankings were issued, so a test can prove a refusal never reached storage.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Gets the window the last read was asked to publish, or <see langword="null" /> when nothing was read.</summary>
    public IReadOnlyList<RankedEmailCandidate>? LastRankedCandidates { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<RankedEmailCandidate>> ReadRankedCandidatesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastSelection = selection;
        this.LastQueryText = queryText;
        this.LastLimit = limit;
        this.ReadCount++;

        IReadOnlyList<RankedEmailCandidate> ranking =
        [
            .. window.Select(match => new RankedEmailCandidate(match.Position, match.RelevanceRank)),
        ];

        return Task.FromResult(ranking);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailSearchMatch>> ReadMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<RankedEmailCandidate> rankedCandidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastSelection = selection;
        this.LastQueryText = queryText;
        this.LastSnippetBounds = snippetBounds;
        this.LastRankedCandidates = rankedCandidates;

        return Task.FromResult<IReadOnlyList<EmailSearchMatch>>(window);
    }
}
