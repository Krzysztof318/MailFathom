// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the lexical search runs, ranks, highlights, and stays parameterized against a real PostgreSQL database.</summary>
/// <remarks>
/// <para>
/// Four claims only this suite can settle. That the <c>tsvector</c> column, <c>websearch_to_tsquery</c>, <c>ts_rank</c>,
/// and <c>ts_headline</c> compose into a command PostgreSQL accepts — an untranslatable expression or a malformed
/// headline option list is a runtime failure, not a compiler error. That a query text carrying SQL metacharacters is
/// data: the command-level test proves it never reaches the SQL, and this proves the server treats what it does reach as
/// search terms. That the snippets come back bounded and marked, which is what the whole snippet contract rests on. And
/// that the projection leaves the change tracker empty.
/// </para>
/// <para>
/// Everything else search decides — which requests are refused, how the window is bounded, how ties are broken — is a
/// decision <c>MailboxSearchReader</c> and the composed command make without a database, and those stay in the unit
/// suites.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailSearchIndexReaderTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "lexical-search";

    private const int SeededEmailCount = 6;

    /// <summary>The term every seeded body carries, so a search for it reaches the whole folder.</summary>
    private const string SharedTerm = "reconciliation";

    /// <summary>The term only one seeded body carries, so a search for it selects that row alone.</summary>
    private const string DistinctiveTerm = "settlement";

    /// <summary>
    /// How many extra times the distinctive row repeats <see cref="SharedTerm" />. Ranking follows how often a document
    /// mentions the query's own words, so repeating a different word would leave the row tied with every other and the
    /// timeline tiebreaker, not the rank, would decide the order.
    /// </summary>
    private const int SharedTermRepetitions = 6;

    /// <summary>A query written the way somebody types one, carrying the metacharacters an injection attempt would.</summary>
    private const string HostileQueryText = "'; DROP TABLE stored_emails; --";

    private static readonly DateTimeOffset FirstReceivedAt = SyntheticEmail.ReceivedAt;

    /// <summary>Runs the composed command three ways and proves it ranks, selects, and refuses to be talked into anything.</summary>
    /// <remarks>
    /// The three searches share one seeded folder and one composition deliberately. Each asks something different of the
    /// same command — that ranking orders the window, that a term one document carries selects that document, and that a
    /// query text of SQL matches nothing and changes nothing — and the failures stay distinguishable because each act
    /// has its own assertion.
    /// </remarks>
    [Fact]
    public async Task ReadRankedMatchesAsync_TermEverySeededBodyCarries_RanksTheBodyMentioningItMostFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await SeededSelectionAsync(services, cancellationToken);

        // Act
        var byDistinctiveTerm = await SearchAsync(services, DistinctiveTerm, cancellationToken);
        var bySharedTerm = await SearchAsync(services, SharedTerm, cancellationToken);
        var byHostileQueryText = await SearchAsync(services, HostileQueryText, cancellationToken);

        // Assert
        var distinctive = Assert.Single(byDistinctiveTerm);
        Assert.True(distinctive.RelevanceRank > 0);

        Assert.Equal(SeededEmailCount, bySharedTerm.Count);
        Assert.Equal(distinctive.Summary.StoredEmailId, bySharedTerm[0].Summary.StoredEmailId);
        Assert.Equal(
            bySharedTerm.Select(match => match.RelevanceRank).OrderDescending(),
            bySharedTerm.Select(match => match.RelevanceRank));

        // The metacharacters reached the server as search terms: they match nothing, and the folder is still there to
        // be searched afterwards. Without the second half an adapter that silently returned nothing would pass.
        Assert.Empty(byHostileQueryText);
        Assert.Equal(SeededEmailCount, (await SearchAsync(services, SharedTerm, cancellationToken)).Count);
    }

    /// <summary>The snippets come back bounded by both counts, marked, and cut from the body rather than replacing it.</summary>
    /// <remarks>
    /// The seeded body puts an unbroken token far longer than the word bound beside the matched term, which is the case
    /// a word count alone cannot bound: <c>MaxWords</c> counts one such token as one word, so without the character
    /// ceiling a snippet of eight words would carry thousands of characters of the message.
    /// </remarks>
    [Fact]
    public async Task ReadRankedMatchesAsync_MatchWithIndexedBodyText_ReturnsSnippetsBoundedByWordsAndCharacters()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var selection = await SeededSelectionAsync(services, cancellationToken);
        var bounds = EmailSearchSnippetBounds.Create(snippetsPerEmail: 2, wordsPerSnippet: 8);

        // Act
        var matches = await SearchAsync(services, DistinctiveTerm, cancellationToken, selection, bounds);

        // Assert
        var match = Assert.Single(matches);
        Assert.NotEmpty(match.Snippets);
        Assert.True(match.Snippets.Count <= bounds.SnippetsPerEmail);
        Assert.All(match.Snippets, snippet =>
        {
            Assert.Contains("**", snippet, StringComparison.Ordinal);
            Assert.True(snippet.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= bounds.WordsPerSnippet);

            var messageCharacters = MessageCharactersOf(snippet);
            Assert.True(
                messageCharacters <= bounds.MaximumCharacters,
                $"A snippet carrying {messageCharacters} characters of the message exceeds the ceiling of {bounds.MaximumCharacters}.");
        });

        // The unbroken token is longer than the ceiling, so at least one snippet had to be cut for the ceiling to be
        // doing anything. Without this the assertion above would pass on a body that never approached it.
        Assert.Contains(match.Snippets, snippet => snippet.EndsWith('…'));
    }

    /// <summary>A search that tracked its rows would let an unrelated commit in the same scope write mail nobody changed.</summary>
    [Fact]
    public async Task ReadMatchesAsync_AnyWindow_TracksNoEntities()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var selection = await SeededSelectionAsync(services, cancellationToken);

        // Act
        var trackedEntityCount = await services.InScopeAsync(
            async (scope, token) =>
            {
                var matches = await RankedWindowAsync(
                    scope.GetRequiredService<IEmailSearchIndexReader>(),
                    selection,
                    SharedTerm,
                    EmailSearchSnippetBounds.Default,
                    token);

                Assert.Equal(SeededEmailCount, matches.Count);

                return scope.GetRequiredService<MailFathomDbContext>().ChangeTracker.Entries().Count();
            },
            cancellationToken);

        // Assert
        Assert.Equal(0, trackedEntityCount);
    }

    /// <summary>Counts what a snippet carries of the message, which is what the character ceiling bounds.</summary>
    /// <remarks>
    /// The highlight markup and the truncation mark are MailFathom's own, so they do not count against a bound that exists
    /// to limit how much of a message one result publishes. Removing every <c>**</c> also removes any the body wrote
    /// itself, which can only make this count lower — so the assertion it feeds stays honest by erring towards passing,
    /// and the ellipsis assertion beside it is what proves the ceiling did any cutting at all.
    /// </remarks>
    private static int MessageCharactersOf(string snippet) => snippet
        .Replace("**", string.Empty, StringComparison.Ordinal)
        .TrimEnd('…')
        .Length;

    private static Task<IReadOnlyList<EmailSearchMatch>> SearchAsync(
        OrchestratedMailFathomServices services,
        string queryText,
        CancellationToken cancellationToken,
        MailboxEmailSelection? selection = null,
        EmailSearchSnippetBounds? snippetBounds = null) => services.InScopeAsync(
            (scope, token) => RankedWindowAsync(
                scope.GetRequiredService<IEmailSearchIndexReader>(),
                selection ?? SeededSelection(scope),
                queryText,
                snippetBounds ?? EmailSearchSnippetBounds.Default,
                token),
            cancellationToken);

    /// <summary>Runs both halves of the port the way the use case does: rank the mail, then read the window it chose.</summary>
    private static async Task<IReadOnlyList<EmailSearchMatch>> RankedWindowAsync(
        IEmailSearchIndexReader reader,
        MailboxEmailSelection selection,
        string queryText,
        EmailSearchSnippetBounds snippetBounds,
        CancellationToken cancellationToken)
    {
        var validatedQueryText = EmailSearchQueryText.Create(queryText);

        var candidates = await reader.ReadRankedCandidatesAsync(
            selection,
            validatedQueryText,
            SeededEmailCount,
            cancellationToken);

        return await reader.ReadMatchesAsync(
            selection,
            validatedQueryText,
            snippetBounds,
            candidates,
            cancellationToken);
    }

    private static MailboxEmailSelection SeededSelection(IServiceProvider scope) => MailboxEmailSelection.Create(
        OrchestratedMailboxScope.Readable(scope, [FolderAlias]),
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen: null,
        isRemotelyFlagged: null,
        keyword: null,
        hasAttachments: null);

    /// <summary>Ensures the seeded folder exists and returns the selection every test in this class searches through.</summary>
    private static async Task<MailboxEmailSelection> SeededSelectionAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        await EnsureSeededAsync(services, binding, cancellationToken);

        return await services.InScopeAsync(
            (scope, _) => Task.FromResult(SeededSelection(scope)),
            cancellationToken);
    }

    /// <summary>Writes the seeded volume once, through the production write path that derives the search documents.</summary>
    /// <remarks>
    /// Seeding is idempotent rather than ordered, because the upsert is keyed by occurrence identity: whichever test runs
    /// first writes the folder and the others find it, so no test depends on having run after another.
    /// </remarks>
    private static async Task EnsureSeededAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        if (await CountSeededEmailsAsync(services, binding, cancellationToken) == SeededEmailCount)
        {
            return;
        }

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var (remoteMetadata, extraction) in SeededEmails(binding))
                {
                    await repository.UpsertMetadataAsync(
                        session, SyntheticMailAccount.Owner,
                        remoteMetadata,
                        extraction,
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Describes the seeded volume: one row whose body repeats a term, and five that mention it once.</summary>
    private static IEnumerable<(RemoteEmailMetadata RemoteMetadata, ExtractedEmailMetadata Extraction)> SeededEmails(
        MailFolderResolution binding) =>
        Enumerable.Range(0, SeededEmailCount).Select(index =>
        {
            var occurrenceId = SyntheticEmail.OccurrenceIn(binding, (uint)(7000 + index));
            var subject = $"search-{index:D4}";
            var extraction = SyntheticEmail.ExtractionOf(
                occurrenceId,
                subject,
                BodyTextOf(index),
                $"recipient{index}@mailfathom.test");

            return (
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                extraction with { ReceivedAt = FirstReceivedAt.AddMinutes(index) });
        });

    /// <summary>The one row that carries the distinctive term and repeats the shared one.</summary>
    private static int DistinctiveRowIndex => SeededEmailCount / 2;

    /// <summary>How many characters each of the over-long words beside the distinctive term carries.</summary>
    /// <remarks>
    /// Below PostgreSQL's own token length limit, above which the parser skips a run rather than indexing it — so these
    /// are words the text search parser accepts and <c>ts_headline</c> therefore emits, not a run it discards.
    /// </remarks>
    private const int OverLongWordCharacters = 400;

    /// <summary>
    /// A run of words each far longer than prose writes, placed beside the distinctive term so the extract cut around
    /// that term has to contain them. Each counts as one word against <c>MaxWords</c>, which is what makes a bound
    /// expressed only in words unable to bound how much of the message an extract carries.
    /// </summary>
    private static IEnumerable<string> OverLongWords =>
        Enumerable.Range(0, 6).Select(index => new string((char)('a' + index), OverLongWordCharacters));

    private static string BodyTextOf(int index)
    {
        var body = SyntheticEmail.BodyTextContaining(SharedTerm, wordCount: 30);

        return index == DistinctiveRowIndex
            ? string.Join(
                ' ',
                Enumerable.Repeat(SharedTerm, SharedTermRepetitions)
                    .Prepend(string.Join(' ', OverLongWords))
                    .Prepend(DistinctiveTerm)
                    .Prepend(body))
            : body;
    }

    private static Task<int> CountSeededEmailsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        return services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .CountAsync(
                    email => email.MailFolder.Alias == alias && email.MailFolder.ResolutionGeneration == generation,
                    token),
            cancellationToken);
    }
}
