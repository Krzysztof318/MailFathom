// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval;

/// <summary>Covers the retrieval a model reaches mail through: what it bounds, what it carries, and what it refuses to send.</summary>
public sealed class MailboxKnowledgeSearchTests
{
    /// <summary>Stands for a filter the selection carries no value for, so a null never reads as a failed comparison.</summary>
    private const string Absent = "-";

    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";


    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SearchedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId[] EveryServedAccount =
    [
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
        MailAccountId.Create("secondary"),
    ];

    /// <summary>The scope a question carrying no narrowing of its own arrives with, which is every served account.</summary>
    /// <remarks>
    /// Never <see cref="MailboxScope.NothingReadable" />: this retrieval is handed a scope somebody already resolved, and
    /// the unrestricted one is what a deployment serving no account at all resolves to.
    /// </remarks>
    private static readonly MailboxScope EveryAccount = MailboxScope.Create(
        SyntheticMailOwner.Deployment,
        EveryServedAccount,
        selectedFolders: null);

    [Fact]
    public async Task FindPassagesAsync_MailMatchingTheQuery_ReturnsOnePassagePerMessageMostRelevantFirst()
    {
        // Arrange
        var mostRelevant = SyntheticEmailSummaries.Create(FirstJuly, subject: "the invoice");
        var lessRelevant = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var index = new InMemoryEmailSearchIndex()
            .With(lessRelevant, relevanceRank: 0.2f, matchedText: "invoice", "a later mention")
            .With(mostRelevant, relevanceRank: 0.9f, matchedText: "invoice", "the invoice is attached");
        var search = SearchOver(index);

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal(
            [mostRelevant.StoredEmailId, lessRelevant.StoredEmailId],
            passages.Select(passage => passage.StoredEmailId));
    }

    /// <summary>An answer that cannot say which message a claim came from cannot be checked.</summary>
    [Fact]
    public async Task FindPassagesAsync_AMatch_CarriesItsIdentityAndSourceCoordinates()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(
            FirstJuly,
            accountId: "secondary",
            folderAlias: "ARCHIVE",
            subject: "the invoice");
        var index = new InMemoryEmailSearchIndex().With(matched, snippets: "the invoice is attached");
        var search = SearchOver(index);

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        var passage = Assert.Single(passages);

        Assert.Equal(matched.StoredEmailId, passage.StoredEmailId);
        Assert.Equal(MailAccountId.Create("secondary"), passage.AccountId);
        Assert.Equal(MailFolderAlias.Create("ARCHIVE"), passage.FolderAlias);
        Assert.Equal("the invoice", passage.Subject);
        Assert.Equal(FirstJuly, passage.ReceivedAt);
        Assert.Equal("the invoice is attached", passage.Text);
    }

    /// <summary>A citation says whether the message it quotes was verified, and this is the hop that carries the verdict to it.</summary>
    [Fact]
    public async Task FindPassagesAsync_AMatch_CarriesTheSenderVerdictItsSummaryWasStoredWith()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(
            FirstJuly,
            senderVerification: new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Trusted,
            });
        var index = new InMemoryEmailSearchIndex().With(matched, snippets: "the invoice is attached");
        var search = SearchOver(index);

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        var passage = Assert.Single(passages);

        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, passage.SenderVerification.AuthorAuthentication);
        Assert.Equal(SenderTrustLevel.Trusted, passage.SenderVerification.DeploymentTrust);
    }

    /// <summary>The count is the bound on how many messages one question can draw on, and it is asked of the search itself.</summary>
    [Fact]
    public async Task FindPassagesAsync_MoreMatchesThanTheBoundAllows_AsksForOnlyThatMany()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        foreach (var email in SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly))
        {
            index.With(email, snippets: "a mention");
        }

        var search = SearchOver(index, EmailKnowledgeBounds.Create(maximumPassages: 3, maximumCharactersPerPassage: 100));

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal(3, passages.Count);
        Assert.Equal(3, Assert.Single(index.RankedCandidatesCalls).Limit);
    }

    [Fact]
    public async Task FindPassagesAsync_AMatchLongerThanOnePassageMayCarry_CutsItToTheBound()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), snippets: new string('a', 400));
        var search = SearchOver(index, EmailKnowledgeBounds.Create(maximumPassages: 4, maximumCharactersPerPassage: 120));

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal(120, Assert.Single(passages).Text.Length);
    }

    /// <summary>
    /// Mail carries emoji and every script outside the basic plane. A cut through a surrogate pair leaves a lone
    /// surrogate, which is not text: it survives no serialization the passage is about to cross.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_ACutThatWouldFallInsideACharacter_TakesTheWholeCharacterInstead()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), snippets: new string('a', 9) + "🧾🧾");
        var search = SearchOver(index, EmailKnowledgeBounds.Create(maximumPassages: 4, maximumCharactersPerPassage: 10));

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        var text = Assert.Single(passages).Text;

        Assert.Equal(9, text.Length);
        Assert.DoesNotContain(text, char.IsSurrogate);
    }

    /// <summary>
    /// A message matched on its subject or its participants while its body yielded no text. Sending its identity with
    /// nothing beside it would spend a model's context on a message it cannot read a word of.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_AMatchWithNoExtracts_IsNotHandedOver()
    {
        // Arrange
        var withText = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), relevanceRank: 0.9f)
            .With(withText, relevanceRank: 0.2f, matchedText: null, "a mention");
        var search = SearchOver(index);

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal(withText.StoredEmailId, Assert.Single(passages).StoredEmailId);
    }

    /// <summary>
    /// A query the search use case refuses travels out as that refusal rather than as an empty window, because the
    /// caller is a tool loop and a model that wrote an unusable value can write a usable one. Every shape the query text
    /// refuses is one answer here: blank text, and text holding a character no document could — both ordinary shapes
    /// free-form model output takes.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("an invoice\nfrom last week")]
    [InlineData("an invoice\u0000from last week")]
    public async Task FindPassagesAsync_AQueryWithNoUsableText_IsRefusedWithoutSearching(string queryText)
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "a mention");
        var search = SearchOver(index);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText(queryText),
            TestContext.Current.CancellationToken));

        Assert.Empty(index.RankedCandidatesCalls);
    }

    /// <summary>The length bound is the one an unbounded free-form query meets first, and it cannot be written inline.</summary>
    [Fact]
    public async Task FindPassagesAsync_AQueryLongerThanOneSearchCarries_IsRefusedWithoutSearching()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "a mention");
        var search = SearchOver(index);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText(new string('a', EmailSearchQueryText.MaximumLength + 1)),
            TestContext.Current.CancellationToken));

        Assert.Empty(index.RankedCandidatesCalls);
    }

    /// <summary>
    /// The reason a run may narrow at all: a question about one person's mail is answered by selecting that person's
    /// mail, rather than by hoping their address outranks every other word in the query.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_ASenderFilter_FindsOnlyThatSendersMail()
    {
        // Arrange
        var fromAnna = SyntheticEmailSummaries.Create(FirstJuly, senderAddress: "anna@example.test");
        var index = new InMemoryEmailSearchIndex()
            .With(fromAnna, relevanceRank: 0.2f, matchedText: "invoice", "anna's mention")
            .With(
                SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), senderAddress: "bruno@example.test"),
                relevanceRank: 0.9f,
                matchedText: "invoice",
                "bruno's mention");
        var search = SearchOver(index);

        var query = new EmailKnowledgeQuery
        {
            QueryText = "invoice",
            SenderAddress = "anna@example.test",
        };

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            query,
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal(fromAnna.StoredEmailId, Assert.Single(passages).StoredEmailId);
    }

    /// <summary>Every filter reaches the validated selection the published search builds, so neither tool narrows differently.</summary>
    [Fact]
    public async Task FindPassagesAsync_EveryFilterANamedQueryCarries_ReachesTheSelectionTheSearchWasGiven()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex();
        var search = SearchOver(index);

        var query = new EmailKnowledgeQuery
        {
            QueryText = "invoice",
            SenderAddress = "anna@example.test",
            RecipientAddress = "bruno@example.test",
            SubjectFragment = "quarterly",
            ReceivedOnOrAfter = FirstJuly,
            ReceivedBefore = FirstJuly.AddDays(7),
            IsRemotelySeen = false,
            IsRemotelyFlagged = true,
            Keyword = "$Label",
            HasAttachments = true,
        };

        // Act
        await search.FindPassagesAsync(EveryAccount, query, TestContext.Current.CancellationToken);

        // Assert
        var selection = Assert.Single(index.RankedCandidatesCalls).Selection;

        Assert.Equal(
            [
                // The comparison form the persistence layer indexes, which is what the published search normalizes to
                // as well: a filter and a stored participant are compared in one form by construction.
                "ANNA@EXAMPLE.TEST",
                "BRUNO@EXAMPLE.TEST",
                "quarterly",
                Written(FirstJuly),
                Written(FirstJuly.AddDays(7)),
                "False",
                "True",
                "$LABEL",
                "True",
            ],
            new[]
            {
                selection.SenderNormalizedAddress ?? Absent,
                selection.RecipientNormalizedAddress ?? Absent,
                selection.SubjectFragment ?? Absent,
                Written(selection.ReceivedOnOrAfter),
                Written(selection.ReceivedBefore),
                selection.IsRemotelySeen?.ToString() ?? Absent,
                selection.IsRemotelyFlagged?.ToString() ?? Absent,
                selection.Keyword ?? Absent,
                selection.HasAttachments?.ToString() ?? Absent,
            });
    }

    /// <summary>A filter the published search refuses is refused here too, named as that search names it.</summary>
    [Fact]
    public async Task FindPassagesAsync_AFilterTheSearchRefuses_IsRefusedRatherThanDropped()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "a mention");
        var search = SearchOver(index);

        var query = new EmailKnowledgeQuery { QueryText = "invoice", SenderAddress = "not an address" };

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => search.FindPassagesAsync(
            EveryAccount,
            query,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("sender address", refusal.FilterName);
        Assert.Empty(index.RankedCandidatesCalls);
    }

    /// <summary>How the mail was ranked decides how a further query is worth wording, so the lookup carries it out.</summary>
    [Fact]
    public async Task FindPassagesAsync_ALexicalDeployment_ReportsHowTheMailWasRanked()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex().With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "a mention");
        var search = SearchOver(index);

        // Act
        var lookup = await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Lexical, lookup.RetrievalMode);
    }

    /// <summary>Writes an instant the one way both sides of an assertion read it, so an offset never decides a comparison.</summary>
    private static string Written(DateTimeOffset? instant) =>
        instant?.ToUniversalTime().ToString("O") ?? Absent;

    /// <summary>The scope decides which mail a question can be answered from, and it comes from the caller rather than the query.</summary>
    [Fact]
    public async Task FindPassagesAsync_AScopeNamingOneAccount_FindsNothingInAnother()
    {
        // Arrange
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly, accountId: "secondary"), snippets: "a mention");
        var search = SearchOver(index);
        var scope = MailboxScope.Create(
            SyntheticMailOwner.Deployment,
            [MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)],
            []);

        // Act
        var passages = (await search.FindPassagesAsync(
            scope,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Empty(passages);
    }

    [Fact]
    public async Task FindPassagesAsync_AScopeNamingAFolder_ForwardsItToTheSearch()
    {
        // Arrange
        var inArchive = SyntheticEmailSummaries.Create(FirstJuly, folderAlias: "ARCHIVE");
        var index = new InMemoryEmailSearchIndex()
            .With(SyntheticEmailSummaries.Create(FirstJuly), snippets: "an inbox mention")
            .With(inArchive, snippets: "an archived mention");
        var search = SearchOver(index);
        var scope = MailboxScope.Create(
            SyntheticMailOwner.Deployment,
            EveryServedAccount,
            [
                new MailFolderIdentity(
                    MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
                    MailFolderAlias.Create("ARCHIVE")),
            ]);

        // Act
        var passages = (await search.FindPassagesAsync(
            scope,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal(inArchive.StoredEmailId, Assert.Single(passages).StoredEmailId);
    }

    /// <summary>
    /// This lookup answers an agent rather than an MCP caller, so the window it reads is deliberately unguarded: the
    /// retrieval that sends these extracts to a model scans them there, under the egress point they actually cross.
    /// Scanning here as well would cost a second scan of every extract and would count text against a series no MCP
    /// caller ever sees.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_AScannerSwitchedOn_LeavesTheWindowToBeGuardedWhereItReachesAModel()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, new FakeTimeProvider(SearchedAt));
        var matched = SyntheticEmailSummaries.Create(FirstJuly, subject: $"the key {Marker}");
        var index = new InMemoryEmailSearchIndex().With(matched, snippets: $"the key is {Marker}");
        var search = SearchOver(index, egressGuard: egress.Guard);

        // Act
        var passage = Assert.Single((await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("key"),
            TestContext.Current.CancellationToken)).Passages);

        // Assert
        Assert.Contains(Marker, passage.Text, StringComparison.Ordinal);
        Assert.Empty(egress.Telemetry.Guarded);
        Assert.Empty(egress.Scanner.ScannedTexts);
    }

    /// <summary>
    /// The retrieval an answering run makes is reached under the grant that admitted the question, and no permission
    /// implies another: requiring the mailbox-read grant here would make one a component of the other, so a credential
    /// granted only the answering permission would stop being able to have its question answered.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_ARunAdmittedUnderTheAnsweringGrantAlone_ReadsTheMailbox()
    {
        // Arrange
        var matched = SyntheticEmailSummaries.Create(FirstJuly, subject: "the invoice");
        var index = new InMemoryEmailSearchIndex()
            .With(matched, relevanceRank: 0.9f, matchedText: "invoice", "the invoice is attached");
        var search = SearchOver(index);

        // Act
        var passages = (await search.FindPassagesAsync(
            EveryAccount,
            EmailKnowledgeQuery.ForText("invoice"),
            TestContext.Current.CancellationToken)).Passages;

        // Assert
        Assert.Equal([matched.StoredEmailId], passages.Select(static passage => passage.StoredEmailId));
    }

    private static MailboxKnowledgeSearch SearchOver(
        InMemoryEmailSearchIndex index,
        EmailKnowledgeBounds? bounds = null,
        SensitiveContentEgressGuard? egressGuard = null) => new(
        new MailboxSearchReader(
            index,
            LexicalOnlySemanticSearch(),
            FreshnessReaderReturningNothing(),
            new MailboxScopeResolver(
                CatalogServing(EveryServedAccount),
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            EmailSearchSnippetBounds.Default,
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            new RecordingMailboxReadTelemetry(),
            // The retrieval an answering run makes is reached under the grant that admitted the question, which is not
            // the mailbox-read grant: the window this walks is read through the internal method, which asks for none.
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk)),
        bounds ?? EmailKnowledgeBounds.Default);

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider and activated nothing.</summary>
    private static SemanticEmailSearch LexicalOnlySemanticSearch() => new(
        Substitute.For<IActiveEmbeddingProfileReader>(),
        new InMemoryEmailVectorSearchIndex(),
        ServingProviderHealth(),
        new FakeTimeProvider(SearchedAt),
        textEmbeddingGenerator: null);

    /// <summary>Reports every provider as answering, so retrieval is decided by the profile and the generator alone.</summary>
    private static IAiProviderHealthReader ServingProviderHealth()
    {
        var reader = Substitute.For<IAiProviderHealthReader>();
        reader.Read(Arg.Any<AiProviderRole>())
            .Returns(call => new AiProviderHealth(
                call.Arg<AiProviderRole>(),
                AiProviderHealthState.Serving,
                SearchedAt));

        return reader;
    }

    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns(
        [
            .. servedAccountIds
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(accountId => SyntheticServedAccount.Of(accountId)),
        ]);

        return catalog;
    }

    private static ISynchronizationFreshnessReader FreshnessReaderReturningNothing()
    {
        var reader = Substitute.For<ISynchronizationFreshnessReader>();
        reader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>([]));

        return reader;
    }
}
