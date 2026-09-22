// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Application.UnitTests.Agent.Search;

/// <summary>The vector spaces the Agent's history search and embedding are exercised against.</summary>
internal static class EmbeddingSpaceExample
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    internal static RegisteredEmbeddingProfile Profile { get; } = new(
        EmbeddingProfileId.Create(new Guid("5b1d2c3e-4f50-4a61-8b72-9c83d4e5f601")),
        Identity());

    internal static ScriptedTextEmbeddingGenerator Generator() => new(Identity(), maximumPassagesPerCall: 8);

    internal static ActiveEmbeddingSpace Serving(ScriptedTextEmbeddingGenerator generator) =>
        new(ProfileReaderReturning(Profile), HealthReader(), new FakeTimeProvider(Now), generator);

    internal static ActiveEmbeddingSpace Inactive() =>
        new(ProfileReaderReturning(null), HealthReader(), new FakeTimeProvider(Now), textEmbeddingGenerator: null);

    private static IActiveEmbeddingProfileReader ProfileReaderReturning(RegisteredEmbeddingProfile? profile)
    {
        var reader = Substitute.For<IActiveEmbeddingProfileReader>();
        reader.FindActiveProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);

        return reader;
    }

    private static IAiProviderHealthReader HealthReader()
    {
        var reader = Substitute.For<IAiProviderHealthReader>();
        reader.Read(Arg.Any<AiProviderRole>())
            .Returns(call => new AiProviderHealth(call.Arg<AiProviderRole>(), AiProviderHealthState.Serving, Now));

        return reader;
    }

    private static EmbeddingProfileIdentity Identity() =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            "a-model",
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));
}
