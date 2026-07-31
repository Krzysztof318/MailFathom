// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Emails;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Answers a search with a fixed window and records what the use case asked for.</summary>
/// <remarks>
/// The window is returned exactly as it was given, whatever limit and whatever bounds the read was issued with. That is
/// deliberate: it is how a test observes the bounds this boundary applies to what it publishes, which would otherwise be
/// invisible behind an adapter that had already applied them.
/// </remarks>
internal sealed class StubEmailSearchIndexReader(params EmailSearchMatch[] window) : IEmailSearchIndexReader
{
    /// <summary>Gets the selection the last read was issued with, or <see langword="null" /> when nothing was read.</summary>
    public MailboxEmailSelection? LastSelection { get; private set; }

    /// <summary>Gets the query text the last read was issued with.</summary>
    public EmailSearchQueryText? LastQueryText { get; private set; }

    /// <summary>Gets the snippet bounds the last read was issued with.</summary>
    public EmailSearchSnippetBounds? LastSnippetBounds { get; private set; }

    /// <summary>Gets the result count the last read asked for.</summary>
    public int LastLimit { get; private set; }

    /// <summary>Gets how many reads were issued, so a test can prove a refusal never reached storage.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailSearchMatch>> ReadRankedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastSelection = selection;
        this.LastQueryText = queryText;
        this.LastSnippetBounds = snippetBounds;
        this.LastLimit = limit;
        this.ReadCount++;

        return Task.FromResult<IReadOnlyList<EmailSearchMatch>>(window);
    }
}
