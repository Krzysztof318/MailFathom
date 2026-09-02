// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers when a deployment offers to answer questions, and when it withholds the offer.</summary>
/// <remarks>
/// Both halves of the AI configuration decide this, so every test here states both. The chat half is expressed as a
/// recorded health state and a moment, because what separates a provider that is failing from one that failed is how
/// long ago the record was written.
/// </remarks>
public sealed class MailAnsweringCapabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20"));

    /// <summary>An instance that declared no chat endpoint answers no questions, which is a supported deployment.</summary>
    [Fact]
    public async Task ReadAsync_NoAnswererRegistered_ReportsAnsweringInactive()
    {
        // Arrange
        var capability = CapabilityOver(answerer: null);

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, availability);
    }

    /// <summary>An instance that embeds no mail has nothing to retrieve a question against, whatever its chat endpoint can do.</summary>
    [Fact]
    public async Task ReadAsync_NoActiveEmbeddingProfile_ReportsAnsweringInactive()
    {
        // Arrange
        var capability = CapabilityOver(new RecordingMailQuestionAnswerer(), embeddingProfileActive: false);

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, availability);
    }

    /// <summary>Vectors exist and nothing can place a question beside them, which is an operator's to repair.</summary>
    [Fact]
    public async Task ReadAsync_AnEmbeddingProfileNothingCanPlaceAQueryIn_ReportsAnsweringDegraded()
    {
        // Arrange
        var capability = CapabilityOver(new RecordingMailQuestionAnswerer(), embeddingGeneratorDeclared: false);

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Degraded, availability);
    }

    [Fact]
    public async Task ReadAsync_BothProvidersConfiguredAndServing_ReportsAnsweringAvailable()
    {
        // Arrange
        var capability = CapabilityOver(new RecordingMailQuestionAnswerer());

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Available, availability);
    }

    /// <summary>A chat endpoint that just refused is not asked again by every client listing the tools.</summary>
    [Theory]
    [InlineData(AiProviderHealthState.Unavailable)]
    [InlineData(AiProviderHealthState.Misconfigured)]
    public async Task ReadAsync_AChatProviderThatFailedAMomentAgo_ReportsAnsweringDegraded(
        AiProviderHealthState state)
    {
        // Arrange
        var capability = CapabilityOver(
            new RecordingMailQuestionAnswerer(),
            chatState: state,
            chatObservedAt: Now - TimeSpan.FromSeconds(5));

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Degraded, availability);
    }

    /// <summary>Nothing else calls the chat endpoint, so a failure that latched would hide the tool for as long as the process ran.</summary>
    [Fact]
    public async Task ReadAsync_AChatProviderWhoseFailureHasAged_ReportsAnsweringAvailableAgain()
    {
        // Arrange
        var capability = CapabilityOver(
            new RecordingMailQuestionAnswerer(),
            chatState: AiProviderHealthState.Misconfigured,
            chatObservedAt: Now - TimeSpan.FromMinutes(5));

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Available, availability);
    }

    /// <summary>A freshly started instance has failed at nothing, and the first question is what establishes the state.</summary>
    [Fact]
    public async Task ReadAsync_AChatProviderNobodyHasCalled_ReportsAnsweringAvailable()
    {
        // Arrange
        var capability = CapabilityOver(
            new RecordingMailQuestionAnswerer(),
            chatState: AiProviderHealthState.Unobserved,
            chatObservedAt: null);

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Available, availability);
    }

    /// <summary>A recorded failure with no moment attached is old rather than fresh, so it can never withhold the tool indefinitely.</summary>
    [Fact]
    public async Task ReadAsync_AChatFailureWithNoMomentRecorded_ReportsAnsweringAvailable()
    {
        // Arrange
        var capability = CapabilityOver(
            new RecordingMailQuestionAnswerer(),
            chatState: AiProviderHealthState.Unavailable,
            chatObservedAt: null);

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Available, availability);
    }

    /// <summary>The embedding half is read even where the chat half already answers, so a listing costs one reading of each.</summary>
    [Fact]
    public async Task ReadAsync_AnEmbeddingProviderThatFailed_ReportsAnsweringDegraded()
    {
        // Arrange
        var capability = CapabilityOver(
            new RecordingMailQuestionAnswerer(),
            embeddingState: AiProviderHealthState.Unavailable);

        // Act
        var availability = await ReadAsync(capability);

        // Assert
        Assert.Equal(MailAnsweringAvailability.Degraded, availability);
    }

    private static Task<MailAnsweringAvailability> ReadAsync(MailAnsweringCapability capability) =>
        capability.ReadAsync(TestContext.Current.CancellationToken);

    /// <summary>Composes the capability over a deployment whose two halves each work unless the test says otherwise.</summary>
    /// <remarks>
    /// The embedding half is expressed as the two decisions an operator makes about it — whether a profile is active and
    /// whether a generator is declared — rather than as its resolved capability, because the capability is what this
    /// arrangement is meant to produce rather than something to hand it.
    /// </remarks>
    private static MailAnsweringCapability CapabilityOver(
        IMailQuestionAnswerer? answerer,
        bool embeddingProfileActive = true,
        bool embeddingGeneratorDeclared = true,
        AiProviderHealthState embeddingState = AiProviderHealthState.Serving,
        AiProviderHealthState chatState = AiProviderHealthState.Serving,
        DateTimeOffset? chatObservedAt = null)
    {
        // Both roles are read through one reader, as the host composes them, so a test that varies one states the other.
        var healthReader = Substitute.For<IAiProviderHealthReader>();
        healthReader.Read(AiProviderRole.Embedding)
            .Returns(new AiProviderHealth(AiProviderRole.Embedding, embeddingState, Now));
        healthReader.Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, chatState, chatObservedAt));

        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(embeddingProfileActive ? ActiveProfile() : null);

        var timeProvider = new FakeTimeProvider(Now);

        return new MailAnsweringCapability(
            new SemanticEmailSearch(
                profileReader,
                new InMemoryEmailVectorSearchIndex(),
                healthReader,
                timeProvider,
                embeddingGeneratorDeclared
                    ? new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8)
                    : null),
            healthReader,
            timeProvider,
            answerer);
    }

    private static RegisteredEmbeddingProfile ActiveProfile() => new(ProfileId, Identity());

    private static EmbeddingProfileIdentity Identity() => EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));
}
