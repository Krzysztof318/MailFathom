// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Search;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Access;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the two rankings the conversation history search fuses, against the columns and statements they read.</summary>
/// <remarks>
/// Both rankings are raw statements over a stored generated search vector and a pgvector column, and which user a row
/// belongs to is decided by a join inside each of them — nothing a substitute reproduces. One test per ranking, because
/// each pays for its own conversation and a failure in one says nothing about the other.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAgentConversationSearchTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A block's fields are found, the entry that matched is named, and another person reading the same words finds
    /// nothing of this person's.
    /// </summary>
    [Fact]
    public async Task ReadLexicalRankingAsync_WordsFromAComposedBlock_FindTheConversationForItsOwnerAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var (conversation, answer) = await AnsweredConversationAsync(host, user, cancellationToken);
            var index = await IndexOfAsync(host, cancellationToken);
            var query = EmailSearchQueryText.Create("indexation cap");

            // Act
            var owned = await index.ReadLexicalRankingAsync(UserId.Create(user), query, limit: 50, cancellationToken);
            var somebodyElses = await index.ReadLexicalRankingAsync(UserId.Create(Guid.NewGuid()), query, limit: 50, cancellationToken);

            // Assert
            var hit = Assert.Single(owned);
            Assert.Equal(conversation, hit.Conversation);
            Assert.Equal(answer, hit.Message);
            Assert.Equal(3L, hit.Sequence);
            Assert.Empty(somebodyElses);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>
    /// A vector recorded twice is recorded once, one recorded for somebody else's conversation is not recorded at all,
    /// the nearest ranking names the entry it was placed from, and deleting the conversation takes its vectors along.
    /// </summary>
    [Fact]
    public async Task ReadNearestRankingAsync_AnEmbeddedQuestion_RanksItForItsOwnerUntilTheConversationIsDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var (conversation, _) = await AnsweredConversationAsync(host, user, cancellationToken);
            var index = await IndexOfAsync(host, cancellationToken);
            var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(host, cancellationToken);
            var profile = new RegisteredEmbeddingProfile(profileId, await ActiveIdentityAsync(host, cancellationToken));
            var owner = UserId.Create(user);

            await index.SaveEmbeddingAsync(conversation, owner, 1, profile, UnitVectorOn(0), cancellationToken);
            await index.SaveEmbeddingAsync(conversation, owner, 1, profile, UnitVectorOn(0), cancellationToken);
            await index.SaveEmbeddingAsync(conversation, UserId.Create(Guid.NewGuid()), 3, profile, UnitVectorOn(0), cancellationToken);

            // Act
            var ranked = await index.ReadNearestRankingAsync(owner, profile, UnitVectorOn(0), limit: 50, cancellationToken);
            var store = await StoreOfAsync(host, cancellationToken);

            Assert.True(await store.TryDeleteAsync(conversation, owner, cancellationToken));
            var afterDeletion = await index.ReadNearestRankingAsync(owner, profile, UnitVectorOn(0), limit: 50, cancellationToken);

            // Assert
            var hit = Assert.Single(ranked);
            Assert.Equal(conversation, hit.Conversation);
            Assert.Equal(1L, hit.Sequence);
            Assert.Empty(afterDeletion);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>Writes a question, the start of its answer, and one composed block, at sequences one to three.</summary>
    private static async Task<(AgentConversationId Conversation, AgentMessageId Answer)> AnsweredConversationAsync(
        OrchestratedMailFathomServices host,
        Guid user,
        CancellationToken cancellationToken)
    {
        var store = await StoreOfAsync(host, cancellationToken);
        var conversation = AgentConversationId.New();
        var owner = UserId.Create(user);
        var answer = AgentMessageId.New();

        Assert.True(await store.TryStartAsync(conversation, owner, Instant, cancellationToken));
        await store.AppendAsync(conversation, owner, Question(), Instant, cancellationToken);
        await store.AppendAsync(conversation, owner, new AgentAnswerStarted(answer), Instant, cancellationToken);
        await store.AppendAsync(conversation, owner, Composed(answer), Instant, cancellationToken);

        return (conversation, answer);
    }

    private static AgentMessageWritten Question() => new(
        AgentMessageId.New(),
        AgentMessageAuthor.Person,
        PresentationText.Create("Where did we land on the price?"),
        AgentMessageScope.Mailbox());

    private static AgentBlockComposed Composed(AgentMessageId answer) => new(
        answer,
        new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationText.Create("The indexation cap stays at five percent."),
            PresentationConfidence.Low));

    private static EmbeddingVector UnitVectorOn(int axis)
    {
        var components = new float[OrchestratedMailFathomServices.DeterministicEmbeddingDimension];
        components[axis] = 1;

        return EmbeddingVector.Create(components);
    }

    private static Task<EmbeddingProfileIdentity> ActiveIdentityAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<ITextEmbeddingGenerator>().Identity),
            cancellationToken);

    private static Task<IAgentConversationSearchIndex> IndexOfAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<IAgentConversationSearchIndex>()),
            cancellationToken);

    private static Task<IAgentConversationStore> StoreOfAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<IAgentConversationStore>()),
            cancellationToken);
}
