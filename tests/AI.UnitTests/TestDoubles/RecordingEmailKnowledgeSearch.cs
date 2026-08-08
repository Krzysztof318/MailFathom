// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Stands in for this deployment's retrieval, recording the scope and query every lookup arrived with.</summary>
/// <remarks>
/// A recorder rather than a substitute, because what the tests using it assert is the scope each call carried — an
/// argument matcher would state the expectation in the assertion instead of reading what actually reached the port.
/// </remarks>
internal sealed class RecordingEmailKnowledgeSearch : IEmailKnowledgeSearch
{
    private readonly List<KnowledgeSearchCall> calls = [];
    private readonly Dictionary<string, IReadOnlyList<EmailKnowledgePassage>> passagesByQuery =
        new(StringComparer.Ordinal);

    /// <summary>Gets what each lookup asked for, in order.</summary>
    public IReadOnlyList<KnowledgeSearchCall> Calls => this.calls;

    /// <summary>Arranges what one query finds.</summary>
    /// <param name="queryText">The query to answer.</param>
    /// <param name="passages">What it finds.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public RecordingEmailKnowledgeSearch Returning(string queryText, params EmailKnowledgePassage[] passages)
    {
        this.passagesByQuery[queryText] = passages;

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailKnowledgePassage>> FindPassagesAsync(
        MailboxScope scope,
        string queryText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new KnowledgeSearchCall(scope, queryText));

        return Task.FromResult(this.passagesByQuery.GetValueOrDefault(queryText, []));
    }

    /// <summary>What one lookup asked for.</summary>
    /// <param name="Scope">The accounts and folders the lookup was restricted to.</param>
    /// <param name="QueryText">The text the model wrote.</param>
    internal sealed record KnowledgeSearchCall(MailboxScope Scope, string QueryText);
}
