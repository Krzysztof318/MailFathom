// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the indexes the schema declares are the ones a timeline read and a lexical search need.</summary>
/// <remarks>
/// <para>
/// Three claims live here that a unit test cannot make. The timeline index has to reproduce
/// <see cref="EmailTimelinePosition.NewestFirst" /> column for column, including the <c>NULLS LAST</c> PostgreSQL would
/// otherwise invert and the <c>uuid</c> tie-break the comparer spells out as sixteen big-endian octets: keyset pages are
/// contiguous only while the server's order and the process's order are the same order. The generated search vector has
/// to be computed by PostgreSQL from the columns beside it. And both indexes have to be chosen by the planner rather
/// than merely exist, which is a statement only a query plan over real data can make.
/// </para>
/// <para>
/// The queries are written here rather than taken from a read model, and they stay that way. Each states something the
/// read model's own query cannot: an <c>ORDER BY</c> with an explicit <c>NULLS LAST</c>, which is what the index
/// declares and what EF Core publishes no way to write, and a parameterized <c>tsquery</c> against the text search
/// configuration the generated column was built with. The assertions are therefore about the schema, which is what
/// carries the coverage marker here. How the mailbox listing read model behaves over the same data is
/// <see cref="OrchestratedStoredEmailTimelineReaderTests" />, and the lexical search read model belongs beside it
/// rather than in this class.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredEmailIndexTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "timeline-and-search";

    /// <summary>
    /// Enough rows for a sequential scan to be the more expensive plan, so a query that reaches for an index is doing so
    /// because the index helps rather than because the table is too small for the choice to matter.
    /// </summary>
    private const int SeededEmailCount = 600;

    /// <summary>How many of the seeded emails carry no received timestamp, which is the group that sorts last.</summary>
    private const int UndatedEmailCount = 20;

    /// <summary>How many emails share one received timestamp, so the identifier tie-break is exercised on every page.</summary>
    private const int EmailsPerReceivedTimestamp = 5;

    private const int PageSize = 50;

    /// <summary>A word that appears in exactly one seeded body, so a search for it is as selective as a real one.</summary>
    private const string DistinctiveBodyTerm = "gyroscopically";

    /// <summary>A word carried only by the subject that also carries the metacharacters, so that row is addressable.</summary>
    private const string MetacharacterSubjectTerm = "zephyrine";

    /// <summary>A subject holding every character a query language or SQL might treat as syntax.</summary>
    private const string MetacharacterSubject =
        $"""{MetacharacterSubjectTerm}'); DROP TABLE stored_emails; -- & | ! <-> :* "quoted" %wildcard%""";

    /// <summary>A query whose words the metacharacter document holds, written as a statement somebody hoped would run.</summary>
    /// <remarks>
    /// Read as data it is a conjunction of the words <c>zephyrine</c>, <c>drop</c>, <c>table</c>, and <c>stored_emails</c>,
    /// every one of which that document's subject carries — so finding exactly that document is what proves the text
    /// reached the index as words. Executed as SQL it would have dropped the table instead, which is what the row count
    /// beside the match reports.
    /// </remarks>
    private const string SqlStatementQueryText = "zephyrine'); DROP TABLE stored_emails; --";

    /// <summary>Query texts that must be read as words, and whose words no seeded document holds.</summary>
    /// <remarks>
    /// The first carries words nothing in the mailbox wrote, and a text search combines a query's terms conjunctively, so
    /// no document can satisfy it. The second is nothing but operators, which <c>websearch_to_tsquery</c> reduces to an
    /// empty query. Both must come back empty rather than raise, match everything, or execute.
    /// </remarks>
    private static readonly string[] UnmatchableQueryTexts =
    [
        "'; DELETE FROM email_search_documents WHERE 1=1; --",
        "& | ! <-> :* ( )",
    ];

    private static readonly string TextSearchConfiguration = PostgresTextSearchConfiguration.Default.Value;

    /// <summary>Reads one page of the folder timeline in the order the folder timeline index declares.</summary>
    private const string FirstTimelinePageSql =
        """
        SELECT "Id", "ReceivedAt"
        FROM stored_emails
        WHERE "MailFolderId" = @folderId
        ORDER BY "ReceivedAt" DESC NULLS LAST, "Id" DESC
        LIMIT @pageSize
        """;

    /// <summary>Resumes the walk after a page that ended on a dated row.</summary>
    /// <remarks>
    /// The row-value comparison is the tie-break written as one expression, so the timestamp and the identifier are
    /// compared in the order the index stores them. Undated rows are admitted unconditionally because every one of them
    /// sorts after every dated row, which is the same decision <c>NULLS LAST</c> states in the ordering.
    /// </remarks>
    private const string DatedTimelineContinuationSql =
        """
        SELECT "Id", "ReceivedAt"
        FROM stored_emails
        WHERE "MailFolderId" = @folderId
          AND ("ReceivedAt" IS NULL OR ("ReceivedAt", "Id") < (@afterReceivedAt, @afterId))
        ORDER BY "ReceivedAt" DESC NULLS LAST, "Id" DESC
        LIMIT @pageSize
        """;

    /// <summary>Resumes the walk once it has reached the undated rows, where the identifier is the whole key.</summary>
    private const string UndatedTimelineContinuationSql =
        """
        SELECT "Id", "ReceivedAt"
        FROM stored_emails
        WHERE "MailFolderId" = @folderId
          AND "ReceivedAt" IS NULL
          AND "Id" < @afterId
        ORDER BY "Id" DESC
        LIMIT @pageSize
        """;

    private const string LexicalSearchSql =
        """
        SELECT "StoredEmailId", NULL::timestamptz AS "ReceivedAt"
        FROM email_search_documents
        WHERE "SearchVector" @@ websearch_to_tsquery(@configuration::regconfig, @queryText)
        """;

    [Fact]
    public async Task ReceivedAtIndexOrder_OverASeededFolderTimeline_IsTheOrderNewestFirstDescribes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var folderId = await SeededFolderIdAsync(services, cancellationToken);

        // Act
        var indexOrder = await ReadTimelineAsync(
            services,
            FirstTimelinePageSql,
            [FolderIdParameter(folderId), PageSizeParameter(SeededEmailCount)],
            cancellationToken);

        // Assert
        Assert.Equal(SeededEmailCount, indexOrder.Count);
        Assert.Equal(UndatedEmailCount, indexOrder.Count(position => position.ReceivedAt is null));

        // The comparer sorted over the same positions the server returned, so a difference is a difference of order
        // rather than of content — which is the only thing keyset pagination needs the two to agree on.
        var comparerOrder = indexOrder.Order(EmailTimelinePosition.NewestFirst).ToArray();
        Assert.Equal(comparerOrder, indexOrder);

        var queryPlan = await ReadQueryPlanAsync(
            services,
            FirstTimelinePageSql,
            [FolderIdParameter(folderId), PageSizeParameter(PageSize)],
            cancellationToken);
        Assert.Contains(PersistenceConstraintNames.StoredEmailFolderTimelineIndexName, queryPlan, StringComparison.Ordinal);
    }

    /// <summary>Proves a keyset walk over that order visits every row exactly once.</summary>
    /// <remarks>
    /// The seeded rows include groups sharing one received timestamp and a group carrying none, because those are the
    /// two shapes a page boundary can fall inside. A tie-break that disagreed with the index, or an ordering that put
    /// undated rows first, would show up here as a row visited twice and another never visited.
    /// </remarks>
    [Fact]
    public async Task KeysetPagination_OverTheFolderTimeline_VisitsEveryRowExactlyOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var folderId = await SeededFolderIdAsync(services, cancellationToken);
        var wholeTimeline = await ReadTimelineAsync(
            services,
            FirstTimelinePageSql,
            [FolderIdParameter(folderId), PageSizeParameter(SeededEmailCount)],
            cancellationToken);

        // Act
        var walkedPages = await WalkTimelineAsync(services, folderId, cancellationToken);

        // Assert
        var walkedPositions = (IReadOnlyList<EmailTimelinePosition>)[.. walkedPages.SelectMany(page => page)];

        Assert.Equal(wholeTimeline, walkedPositions);
        Assert.Equal(walkedPositions.Count, walkedPositions.Select(position => position.StoredEmailId).Distinct().Count());

        // A full page followed by pages until one comes back short: the walk must end because the timeline ran out, not
        // because a page happened to be empty in the middle of it.
        Assert.All(walkedPages.SkipLast(1), page => Assert.Equal(PageSize, page.Count));
        Assert.True(walkedPages[^1].Count <= PageSize);
    }

    [Fact]
    public async Task SearchVector_ForATermCarriedByOneDocument_FindsItThroughTheLexicalIndex()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        await EnsureSeededAsync(services, binding, cancellationToken);

        // Act
        var bodyTermMatches = await SearchAsync(services, DistinctiveBodyTerm, cancellationToken);
        var subjectTermMatches = await SearchAsync(services, MetacharacterSubjectTerm, cancellationToken);

        // Assert
        Assert.Single(bodyTermMatches);
        Assert.Single(subjectTermMatches);

        // Both halves of the document are covered by one vector, so the body term and the subject term have to reach
        // different rows: one match each proves the generated column is built from every column it names.
        Assert.NotEqual(bodyTermMatches[0], subjectTermMatches[0]);

        var queryPlan = await ReadQueryPlanWithoutSequentialScansAsync(
            services,
            LexicalSearchSql,
            [ConfigurationParameter(), QueryTextParameter(DistinctiveBodyTerm)],
            cancellationToken);
        Assert.Contains(PersistenceConstraintNames.EmailSearchDocumentVectorIndexName, queryPlan, StringComparison.Ordinal);
    }

    /// <summary>Proves query text carrying SQL and full-text syntax is data on the way in and on the way out.</summary>
    /// <remarks>
    /// The stored subject carries the same characters, so the claim covers both directions: a document holding them is
    /// indexed, and a query holding them is parsed into words by <c>websearch_to_tsquery</c> rather than reaching the
    /// server as syntax. A statement that had been executed instead would show up as a table that is no longer there or
    /// no longer full, which is what the count beside the matches reports.
    /// </remarks>
    [Fact]
    public async Task SearchVector_ForQueryTextCarryingSqlAndFullTextSyntax_ReadsItAsWordsAndLeavesTheDataIntact()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        await EnsureSeededAsync(services, binding, cancellationToken);
        var metacharacterDocument = Assert.Single(
            await SearchAsync(services, MetacharacterSubjectTerm, cancellationToken));

        // Act
        var statementMatches = await SearchAsync(services, SqlStatementQueryText, cancellationToken);

        var unmatchableResults = new List<IReadOnlyList<EmailTimelinePosition>>();
        foreach (var queryText in UnmatchableQueryTexts)
        {
            unmatchableResults.Add(await SearchAsync(services, queryText, cancellationToken));
        }

        // Assert
        // Read as words, the statement is a conjunction the metacharacter document's own subject satisfies and no other
        // document does, so the one row it returns is the proof that it was words.
        Assert.Equal(metacharacterDocument, Assert.Single(statementMatches));

        Assert.All(unmatchableResults, Assert.Empty);
        Assert.Equal(SeededEmailCount, await CountSeededDocumentsAsync(services, binding, cancellationToken));
    }

    private static async Task<IReadOnlyList<IReadOnlyList<EmailTimelinePosition>>> WalkTimelineAsync(
        OrchestratedMailFathomServices services,
        long folderId,
        CancellationToken cancellationToken)
    {
        var pages = new List<IReadOnlyList<EmailTimelinePosition>>();
        EmailTimelinePosition? resumeAfter = null;

        do
        {
            var page = await ReadTimelinePageAsync(services, folderId, resumeAfter, cancellationToken);

            pages.Add(page);
            resumeAfter = page.Count > 0 ? page[^1] : null;
        }
        while (pages[^1].Count == PageSize);

        return pages;
    }

    /// <summary>Reads the page that follows one position, choosing the continuation the cursor's own shape requires.</summary>
    private static Task<IReadOnlyList<EmailTimelinePosition>> ReadTimelinePageAsync(
        OrchestratedMailFathomServices services,
        long folderId,
        EmailTimelinePosition? resumeAfter,
        CancellationToken cancellationToken) => resumeAfter switch
        {
            null => ReadTimelineAsync(
                services,
                FirstTimelinePageSql,
                [FolderIdParameter(folderId), PageSizeParameter(PageSize)],
                cancellationToken),
            { ReceivedAt: { } receivedAt } position => ReadTimelineAsync(
                services,
                DatedTimelineContinuationSql,
                [
                    FolderIdParameter(folderId),
                    PageSizeParameter(PageSize),
                    new NpgsqlParameter<DateTimeOffset>("afterReceivedAt", receivedAt),
                    new NpgsqlParameter<Guid>("afterId", position.StoredEmailId.Value),
                ],
                cancellationToken),
            { } position => ReadTimelineAsync(
                services,
                UndatedTimelineContinuationSql,
                [
                    FolderIdParameter(folderId),
                    PageSizeParameter(PageSize),
                    new NpgsqlParameter<Guid>("afterId", position.StoredEmailId.Value),
                ],
                cancellationToken),
        };

    private static Task<IReadOnlyList<EmailTimelinePosition>> SearchAsync(
        OrchestratedMailFathomServices services,
        string queryText,
        CancellationToken cancellationToken) => ReadTimelineAsync(
            services,
            LexicalSearchSql,
            [ConfigurationParameter(), QueryTextParameter(queryText)],
            cancellationToken);

    private static NpgsqlParameter<long> FolderIdParameter(long folderId) => new("folderId", folderId);

    private static NpgsqlParameter<int> PageSizeParameter(int pageSize) => new("pageSize", pageSize);

    private static NpgsqlParameter<string> ConfigurationParameter() => new("configuration", TextSearchConfiguration);

    private static NpgsqlParameter<string> QueryTextParameter(string queryText) => new("queryText", queryText);

    /// <summary>Ensures the seeded volume exists and returns the folder its rows hang from.</summary>
    private static async Task<long> SeededFolderIdAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        await EnsureSeededAsync(services, binding, cancellationToken);

        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        return await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .MailFolders
                .AsNoTracking()
                .Where(folder => folder.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                    && folder.Alias == alias
                    && folder.ResolutionGeneration == generation)
                .Select(folder => folder.Id)
                .SingleAsync(token),
            cancellationToken);
    }

    /// <summary>Writes the seeded volume once, through the production write path.</summary>
    /// <remarks>
    /// Seeding is idempotent rather than ordered: the upsert is keyed by occurrence identity, so a class whose tests each
    /// arrange the same volume writes it once and finds it afterwards, and no test depends on having run after another.
    /// The statistics update is part of the arrangement, because a planner with no statistics for a freshly filled table
    /// chooses a plan from defaults and the plan assertions would describe that rather than the indexes.
    /// </remarks>
    private static async Task EnsureSeededAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        if (await CountSeededDocumentsAsync(services, binding, cancellationToken) == SeededEmailCount)
        {
            return;
        }

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var seededEmail in SeededEmails(binding))
                {
                    await repository.UpsertMetadataAsync(
                        session,
                        seededEmail.RemoteMetadata,
                        seededEmail.Extraction,
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var database = scope.GetRequiredService<MailFathomDbContext>().Database;

                return await database.ExecuteSqlRawAsync("ANALYZE stored_emails, email_search_documents", token);
            },
            cancellationToken);
    }

    /// <summary>Describes the seeded volume: dated groups, an undated tail, and the two addressable documents.</summary>
    private static IEnumerable<SeededEmail> SeededEmails(MailFolderResolution binding) =>
        Enumerable.Range(0, SeededEmailCount).Select(index =>
        {
            var occurrenceId = SyntheticEmail.OccurrenceIn(binding, (uint)(1000 + index));
            var subject = index == SeededEmailCount / 2
                ? MetacharacterSubject
                : $"timeline-{index:D4}";
            var bodyTerm = index == SeededEmailCount / 3 ? DistinctiveBodyTerm : $"body{index}";

            return new SeededEmail(
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                SyntheticEmail.ExtractionOf(
                    occurrenceId,
                    subject,
                    SyntheticEmail.BodyTextContaining(bodyTerm, wordCount: 120),
                    $"recipient{index % 8}@mailfathom.test") with
                {
                    ReceivedAt = ReceivedAtOf(index),
                });
        });

    /// <summary>Groups received timestamps so ties are common, and leaves the last rows without one at all.</summary>
    private static DateTimeOffset? ReceivedAtOf(int index) => index >= SeededEmailCount - UndatedEmailCount
        ? null
        : SyntheticEmail.ReceivedAt.AddMinutes(index / EmailsPerReceivedTimestamp);

    private static Task<int> CountSeededDocumentsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        return services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailSearchDocuments
                .AsNoTracking()
                .CountAsync(
                    document => document.StoredEmail.MailFolder.Alias == alias
                        && document.StoredEmail.MailFolder.ResolutionGeneration == generation,
                    token),
            cancellationToken);
    }

    /// <summary>Runs a parameterized read and projects its two columns onto timeline positions.</summary>
    private static Task<IReadOnlyList<EmailTimelinePosition>> ReadTimelineAsync(
        OrchestratedMailFathomServices services,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken) => OrchestratedQueryPlans.WithConnectionAsync(
            services,
            async (connection, token) =>
            {
                await using var command = OrchestratedQueryPlans.CreateCommand(connection, sql, parameters);

                var positions = new List<EmailTimelinePosition>();

                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    positions.Add(new EmailTimelinePosition(
                        await reader.IsDBNullAsync(1, token)
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(1),
                        StoredEmailId.Create(reader.GetFieldValue<Guid>(0))));
                }

                return (IReadOnlyList<EmailTimelinePosition>)positions;
            },
            cancellationToken);

    /// <summary>Reads the plan PostgreSQL chooses for a query when nothing constrains the planner.</summary>
    private static Task<string> ReadQueryPlanAsync(
        OrchestratedMailFathomServices services,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken) =>
        OrchestratedQueryPlans.ReadAsync(services, sql, parameters, cancellationToken);

    /// <summary>Reads the plan PostgreSQL chooses once a sequential scan is not available to it.</summary>
    /// <remarks>
    /// This asks whether an index can serve the query at all, which is a property of the schema, rather than whether the
    /// planner prefers it, which is a property of the data. A lexical index only earns its cost at a volume this suite
    /// deliberately does not seed — a few hundred rows fit in a handful of pages, so scanning them really is the cheaper
    /// plan and a planner that chose it would be right. Taking the sequential scan away leaves the question the schema
    /// answers: is there an index this query shape can use, or would a full mailbox be scanned for every search.
    /// </remarks>
    private static Task<string> ReadQueryPlanWithoutSequentialScansAsync(
        OrchestratedMailFathomServices services,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken) => OrchestratedQueryPlans.ReadAsync(
            services,
            sql,
            parameters,
            ["SET LOCAL enable_seqscan = off"],
            cancellationToken);

    /// <summary>One seeded email, as the two metadata sources a synchronization run would have produced.</summary>
    private sealed record SeededEmail(RemoteEmailMetadata RemoteMetadata, ExtractedEmailMetadata Extraction);
}
