// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves that the vector ranking runs against a real pgvector index and orders by what the distance operator actually
/// returns.
/// </summary>
/// <remarks>
/// Neither claim is reachable without a real server. The ranking is a correlated minimum over a message's own passages,
/// measured by a pgvector operator inside an ordering clause; a substitute would answer whatever the arrangement said
/// and would report the order as correct however the statement had been composed. The vectors are written directly so
/// that the distances are the test's own values rather than a generator's, which is what lets the expected order be
/// stated rather than guessed.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailVectorSearchTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "vector-search";

    /// <summary>
    /// The whole live path: the caller's filters narrow the mail, every eligible message is measured by its nearest
    /// embedded passage, mail with no vector under the profile is absent rather than distant, and the window comes back
    /// nearest first. Written as one test because it is one statement, and splitting it would pay for the same
    /// arrangement four times.
    /// </summary>
    [Fact]
    public async Task ReadNearestCandidatesAsync_MailEmbeddedUnderTheProfile_IsRankedNearestFirstWithinTheFilters()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        var profile = new RegisteredEmbeddingProfile(profileId, await ActiveIdentityAsync(services, cancellationToken));

        var nearest = await StoreOneMessageAsync(services, binding, uid: 9601, cancellationToken);
        var farther = await StoreOneMessageAsync(services, binding, uid: 9602, cancellationToken);
        var unembedded = await StoreOneMessageAsync(services, binding, uid: 9603, cancellationToken);

        // The query sits on the first axis, so a passage on that axis is nearest and one on the second is farther.
        await StoreEveryVectorAsync(services, nearest, profileId, axis: 0, cancellationToken);
        await StoreEveryVectorAsync(services, farther, profileId, axis: 1, cancellationToken);

        // Act
        var candidates = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailVectorSearchIndexReader>().ReadNearestCandidatesAsync(
                SelectionOf(scope, binding),
                profile,
                QueryVector(),
                limit: 50,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(
            [nearest, farther],
            candidates.Select(candidate => candidate.StoredEmailId));
        Assert.DoesNotContain(unembedded, candidates.Select(candidate => candidate.StoredEmailId));
        Assert.True(candidates[0].Score < candidates[1].Score);
    }

    /// <summary>Reads the identity the deterministic generator produces, which is the one the active profile records.</summary>
    private static Task<EmbeddingProfileIdentity> ActiveIdentityAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<ITextEmbeddingGenerator>().Identity),
            cancellationToken);

    /// <summary>Places the query on the first axis of the profile's space.</summary>
    private static EmbeddingVector QueryVector()
    {
        var components = new float[OrchestratedMailFathomServices.DeterministicEmbeddingDimension];
        components[0] = 1;

        return EmbeddingVector.Create(components);
    }

    private static MailboxEmailSelection SelectionOf(IServiceProvider scope, MailFolderResolution binding) =>
        MailboxEmailSelection.Create(
        OrchestratedMailboxScope.Readable(scope, [binding.Alias.Value]),
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen: null,
        isRemotelyFlagged: null,
        keyword: null,
        hasAttachments: null);

    /// <summary>Stores one synthetic message and cuts it, which is the state an account run leaves one in.</summary>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"vector-search-{uid}";
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
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 200),
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // The passages every vector below hangs on, cut in their own transaction because storing no longer cuts.
        await OrchestratedPassages.CutAsync(services, storedEmailId, cancellationToken);

        return storedEmailId;
    }

    /// <summary>Writes one vector per passage of a message, all of them on the named axis.</summary>
    /// <returns>How many rows the write produced, which the caller reads only through the assertions that follow it.</returns>
    private static Task<int> StoreEveryVectorAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        int axis,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var generatedAt = TimeProvider.System.GetUtcNow();

                var chunkIds = await context.EmailChunks
                    .AsNoTracking()
                    .Where(chunk => chunk.StoredEmailId == storedEmailId.Value)
                    .Select(chunk => chunk.Id)
                    .ToArrayAsync(token);

                Assert.NotEmpty(chunkIds);

                context.EmailEmbeddings.AddRange(chunkIds.Select(chunkId => new EmailEmbeddingEntity
                {
                    EmailChunkId = chunkId,
                    EmbeddingProfileId = profileId.Value,
                    Dimension = OrchestratedMailFathomServices.DeterministicEmbeddingDimension,
                    Embedding = new Vector(UnitVectorOn(axis)),
                    GeneratedAt = generatedAt,
                }));

                return await context.SaveChangesAsync(token);
            },
            cancellationToken);

    private static float[] UnitVectorOn(int axis)
    {
        var components = new float[OrchestratedMailFathomServices.DeterministicEmbeddingDimension];
        components[axis] = 1;

        return components;
    }
}
