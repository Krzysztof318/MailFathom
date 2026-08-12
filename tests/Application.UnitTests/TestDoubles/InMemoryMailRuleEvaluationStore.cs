// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An account's stored mail as a rule pass sees it, in insertion order.</summary>
/// <remarks>
/// Insertion order stands in for the identity ordering PostgreSQL walks, which is what both keyset reads rely on. The
/// evaluation record is a field of the row rather than a separate table, exactly as it is in the schema, so a test can
/// arrange mail that has already been evaluated and read back what a pass wrote.
/// </remarks>
internal sealed class InMemoryMailRuleEvaluationStore : IMailRuleEvaluationStore
{
    private readonly List<StoredRow> rows = [];
    private readonly List<StoredEmailId> bodyTextReads = [];
    private readonly List<StoredEmailId> evaluated = [];

    /// <summary>Gets the identities whose body text a condition actually asked for.</summary>
    internal IReadOnlyList<StoredEmailId> BodyTextReads => this.bodyTextReads;

    /// <summary>Gets the identities a pass has recorded an evaluation for, in the order it recorded them.</summary>
    internal IReadOnlyList<StoredEmailId> Evaluated => this.evaluated;

    /// <summary>Adds one stored email and answers with the identity a pass will see it under.</summary>
    /// <param name="facts">The metadata a condition reads.</param>
    /// <param name="awaitsExtraction">Whether text is still expected to be derived from the email's content.</param>
    /// <param name="bodyText">The extracted text a condition naming the body text resolves.</param>
    /// <param name="evaluatedAt">When a pass last evaluated it, which takes it out of the arrival queue.</param>
    /// <returns>The identity of the added email.</returns>
    internal StoredEmailId Add(
        MailRuleEmailFacts facts,
        bool awaitsExtraction = false,
        string? bodyText = null,
        DateTimeOffset? evaluatedAt = null)
    {
        var row = new StoredRow
        {
            Id = StoredEmailId.Create(Guid.CreateVersion7()),
            Facts = facts,
            AwaitsExtraction = awaitsExtraction,
            BodyText = bodyText,
            EvaluatedAt = evaluatedAt,
        };

        this.rows.Add(row);

        return row.Id;
    }

    /// <summary>Reports whether a pass has recorded an evaluation for one email.</summary>
    /// <param name="storedEmailId">The email to ask about.</param>
    /// <returns><see langword="true" /> when the email carries an evaluation record.</returns>
    internal bool IsEvaluated(StoredEmailId storedEmailId) =>
        this.rows.Single(row => row.Id == storedEmailId).EvaluatedAt is not null;

    /// <summary>Lets an email that was waiting for its text become eligible, as extraction committing would.</summary>
    /// <param name="storedEmailId">The email whose text has arrived.</param>
    /// <param name="bodyText">The text extraction produced.</param>
    internal void CompleteExtraction(StoredEmailId storedEmailId, string bodyText)
    {
        var row = this.rows.Single(candidate => candidate.Id == storedEmailId);

        row.AwaitsExtraction = false;
        row.BodyText = bodyText;
        row.Facts = row.Facts with { HasExtractedContent = true };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> GetEmailsAwaitingFirstEvaluationAsync(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.Read(accountId, resumeAfter, batchSize, row => row.EvaluatedAt is null));

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> GetStoredEmailsAsync(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.Read(accountId, resumeAfter, batchSize, static _ => true));

    /// <inheritdoc />
    public Task<string?> ReadExtractedBodyTextAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        this.bodyTextReads.Add(storedEmailId);

        return Task.FromResult(this.rows.Single(row => row.Id == storedEmailId).BodyText);
    }

    /// <inheritdoc />
    public Task RecordEvaluatedAsync(
        IPersistenceSession session,
        IReadOnlyList<StoredEmailId> storedEmailIds,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        foreach (var storedEmailId in storedEmailIds)
        {
            this.rows.Single(candidate => candidate.Id == storedEmailId).EvaluatedAt = evaluatedAt;
            this.evaluated.Add(storedEmailId);
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<StoredEmailAwaitingRuleEvaluation> Read(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        Func<StoredRow, bool> admits)
    {
        var startIndex = resumeAfter is { } position
            ? this.rows.FindIndex(row => row.Id == position) + 1
            : 0;

        return
        [
            .. this.rows
                .Skip(startIndex)
                .Where(row => row.Facts.Account == accountId.Value && admits(row))
                .Take(batchSize)
                .Select(row => new StoredEmailAwaitingRuleEvaluation(row.Id, row.Facts, row.AwaitsExtraction)),
        ];
    }

    private sealed class StoredRow
    {
        internal required StoredEmailId Id { get; init; }

        internal required MailRuleEmailFacts Facts { get; set; }

        internal bool AwaitsExtraction { get; set; }

        internal string? BodyText { get; set; }

        internal DateTimeOffset? EvaluatedAt { get; set; }
    }
}
