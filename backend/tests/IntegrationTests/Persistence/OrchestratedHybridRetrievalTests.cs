// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the whole retrieval chain over one real database: text is cut into passages, embedded, ranked by both the
/// full-text index and pgvector, fused, and handed over as the passages an answer is built from.
/// </summary>
/// <remarks>
/// <para>
/// Each half of the ranking already has a test of its own, and neither of them reaches what this class is about. The
/// two halves are separate queries over separate indexes producing separate row shapes, and fusion only means anything
/// if they agree on the identity of a message; a disagreement would leave one message counted twice, ranked as two, and
/// published as two results — which no substitute can show, because a substitute is where the agreement is assumed.
/// </para>
/// <para>
/// Every vector here comes from the in-repository deterministic generator, so the chain from chunking to a passage runs
/// end to end at no provider cost. What the generator places where is nothing this class asserts on: the claims below
/// are about which mail each half can reach and what survives the fusion, never about which of two messages a hash put
/// nearer to a query.
/// </para>
/// <para>
/// Two tests over one seeded folder, because the second asks what the first's window becomes on the way to a model, and
/// paying for the seeding twice would buy nothing. The seeding is idempotent for the reason the lexical class's is:
/// whichever test runs first writes the folder and the other finds it.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedHybridRetrievalTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "hybrid-retrieval";

    /// <summary>The word the query is written as, carried by one seeded body and no other.</summary>
    private const string QueryTerm = "reconciliation";

    /// <summary>The subject of the message the full-text index can reach, which is the one that carries the term.</summary>
    private const string LexicallyReachableSubject = "hybrid-retrieval-lexical";

    /// <summary>The subject of the message only the vector index can reach, whose body shares no word with the query.</summary>
    private const string SemanticallyReachableSubject = "hybrid-retrieval-semantic";

    /// <summary>The subject of the message neither half can reach: no query term in its body and no vector under the profile.</summary>
    private const string UnreachableSubject = "hybrid-retrieval-unreachable";

    /// <summary>
    /// A hybrid instance answers from both indexes at once: the message carrying the query's word arrives from the
    /// full-text ranking, the message sharing no word with the query arrives from the vector ranking, and each of them
    /// arrives once. The lexical ranking is run alone beside it, which is what makes "this one came from the other half"
    /// a fact rather than an assumption.
    /// </summary>
    [Fact]
    public async Task SearchEmailsAsync_AnInstanceThatEmbeds_FusesBothRankingsAndPublishesEachMessageOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var seeded = await SeededMailboxAsync(services, cancellationToken);

        // Act
        var hybrid = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxSearchReader>().SearchEmailsAsync(
                SearchRequest(),
                token),
            [MailFathomPermission.MailRead],
            cancellationToken);
        var lexicalOnly = await LexicalCandidatesAsync(services, cancellationToken);

        // Assert
        Assert.Equal(EmailSearchRetrievalMode.Hybrid, hybrid.RetrievalMode);
        Assert.Equal(SemanticSearchCapability.Available, hybrid.SemanticSearch);

        var found = hybrid.Matches.Select(match => match.Summary.StoredEmailId).ToArray();

        // The control: the full-text ranking alone reaches the message carrying the term and nothing else, so the
        // second message in the fused window can only have come from the vector ranking beside it.
        Assert.Equal(seeded.LexicallyReachable, Assert.Single(lexicalOnly));

        Assert.Contains(seeded.LexicallyReachable, found);
        Assert.Contains(seeded.SemanticallyReachable, found);

        // Mail with no vector under the active profile and no word in common with the query is reached by neither half,
        // which is what says the window is a ranking rather than the folder.
        Assert.DoesNotContain(seeded.Unreachable, found);

        // The claim fusion rests on: both halves name a message by the same identity, so a message both of them found
        // is one result rather than two.
        Assert.Equal(found.Distinct(), found);
    }

    /// <summary>
    /// What a run hands a model: bounded extracts, each naming mail this database actually holds. A citation is only
    /// worth anything if the identifier beside the text resolves, and whether it does is decided by the two rankings and
    /// the row the summary was projected from rather than by anything a substitute could answer.
    /// </summary>
    [Fact]
    public async Task FindPassagesAsync_OverTheSameHybridRetrieval_YieldsBoundedPassagesNamingStoredMail()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var seeded = await SeededMailboxAsync(services, cancellationToken);
        var bounds = await services.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<EmailKnowledgeBounds>()),
            cancellationToken);

        // Act
        var lookup = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxKnowledgeSearch>().FindPassagesAsync(
                OrchestratedMailboxScope.Readable(scope, [FolderAlias]),
                EmailKnowledgeQuery.ForText(QueryTerm),
                token),
            cancellationToken);

        // Assert
        Assert.NotEmpty(lookup.Passages);
        Assert.True(lookup.Passages.Count <= bounds.MaximumPassages);
        Assert.Contains(seeded.LexicallyReachable, lookup.Passages.Select(passage => passage.StoredEmailId));

        Assert.All(lookup.Passages, passage =>
        {
            Assert.Equal(SyntheticMailAccount.AccountId, passage.AccountId);
            Assert.Equal(MailFolderAlias.Create(FolderAlias), passage.FolderAlias);
            Assert.NotEmpty(passage.Text);
            Assert.True(
                passage.Text.Length <= bounds.MaximumCharactersPerPassage,
                $"A passage carrying {passage.Text.Length} characters exceeds the ceiling of {bounds.MaximumCharactersPerPassage}.");
        });

        // Every identifier a model would be asked to cite resolves to a row this database holds. A passage naming mail
        // that is not there would be a citation nobody could follow, and only a real read establishes that none is.
        var cited = lookup.Passages.Select(passage => passage.StoredEmailId).Distinct().ToArray();
        Assert.Equal(cited.Length, await CountStoredAsync(services, cited, cancellationToken));
    }

    /// <summary>Runs the full-text ranking on its own, which is what the fused window is read against.</summary>
    private static Task<IReadOnlyList<StoredEmailId>> LexicalCandidatesAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var candidates = await scope.GetRequiredService<IEmailSearchIndexReader>().ReadRankedCandidatesAsync(
                    SeededSelection(scope),
                    EmailSearchQueryText.Create(QueryTerm),
                    limit: 50,
                    token);

                return (IReadOnlyList<StoredEmailId>)[.. candidates.Select(candidate => candidate.StoredEmailId)];
            },
            cancellationToken);

    private static SearchEmailsRequest SearchRequest() => new()
    {
        QueryText = QueryTerm,
        Accounts = [MailAccountSelector.For(SyntheticMailAccount.AccountId)],
        Folders = [MailFolderReference.ToAlias(MailFolderAlias.Create(FolderAlias))],
        ResultLimit = 10,
    };

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

    /// <summary>Counts how many of the named messages this database actually holds.</summary>
    private static Task<int> CountStoredAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        Guid[] identifiers = [.. storedEmailIds.Select(storedEmailId => storedEmailId.Value)];

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .CountAsync(email => identifiers.Contains(email.Id), token),
            cancellationToken);
    }

    /// <summary>Ensures the three seeded messages exist, two of them embedded, and names each of them.</summary>
    /// <remarks>
    /// Written through the production paths throughout: the metadata repository derives the passages, and the embedding
    /// generator writes the vectors under whichever profile is active. Nothing here shapes a row by hand, so a
    /// disagreement between the two rankings cannot be arranged away.
    /// </remarks>
    private static async Task<SeededMailbox> SeededMailboxAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        var lexicallyReachable = await StoreAsync(
            services,
            binding,
            uid: 9701,
            LexicallyReachableSubject,
            SyntheticEmail.BodyTextContaining(QueryTerm, wordCount: 40),
            cancellationToken);
        var semanticallyReachable = await StoreAsync(
            services,
            binding,
            uid: 9702,
            SemanticallyReachableSubject,
            SyntheticEmail.BodyTextContaining("settlement", wordCount: 40),
            cancellationToken);
        var unreachable = await StoreAsync(
            services,
            binding,
            uid: 9703,
            UnreachableSubject,
            SyntheticEmail.BodyTextContaining("custody", wordCount: 40),
            cancellationToken);

        await EmbedAsync(services, lexicallyReachable, cancellationToken);
        await EmbedAsync(services, semanticallyReachable, cancellationToken);

        return new SeededMailbox(lexicallyReachable, semanticallyReachable, unreachable);
    }

    /// <summary>Stores one synthetic message and cuts it, which is the state an account run leaves one in.</summary>
    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        string subject,
        string bodyText,
        CancellationToken cancellationToken)
    {
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    SyntheticEmail.ExtractionOf(
                        occurrenceId,
                        subject,
                        bodyText,
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // The passages the semantic half ranks over, cut in their own transaction because storing no longer cuts.
        await OrchestratedPassages.CutAsync(services, storedEmailId, cancellationToken);

        return storedEmailId;
    }

    /// <summary>Embeds every passage of one message under the active profile, which a repeat run finds nothing to do for.</summary>
    private static Task<StoredEmailEmbeddingRun> EmbedAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var serving = await scope.GetRequiredService<IActiveEmbeddingProfileReader>()
                    .FindActiveProfileAsync(token);

                return await scope.GetRequiredService<StoredEmailEmbeddingGenerator>().EmbedAsync(
                    storedEmailId,
                    Assert.IsType<RegisteredEmbeddingProfile>(serving),
                    token);
            },
            cancellationToken);

    /// <summary>The three messages the two tests read, named by which half of the retrieval can reach each.</summary>
    private sealed record SeededMailbox(
        StoredEmailId LexicallyReachable,
        StoredEmailId SemanticallyReachable,
        StoredEmailId Unreachable);
}
