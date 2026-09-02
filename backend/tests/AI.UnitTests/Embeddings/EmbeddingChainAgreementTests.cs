// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.AI.UnitTests.Embeddings;

/// <summary>Covers the rule that a fallback reaches the same vector space or the chain is refused.</summary>
public sealed class EmbeddingChainAgreementTests
{
    /// <summary>The case the chain exists for: one model offered by two providers, reached at two addresses.</summary>
    [Fact]
    public void FindDisagreement_OneModelAtTwoAddresses_ReportsNoDisagreement()
    {
        // Arrange
        var chain = new[]
        {
            EmbeddingDeclarations.Endpoint("first-party", address: "https://api.provider.invalid/v1/"),
            EmbeddingDeclarations.Endpoint(
                "cloud-deployment",
                address: "https://resource.cloud.invalid/openai/v1/",
                routedModelName: "embeddings-small"),
        };

        // Act
        var disagreement = EmbeddingChainAgreement.FindDisagreement(chain);

        // Assert
        Assert.Null(disagreement);
    }

    [Fact]
    public void FindDisagreement_OneEndpoint_ReportsNoDisagreement()
    {
        // Act
        var disagreement = EmbeddingChainAgreement.FindDisagreement([EmbeddingDeclarations.Endpoint()]);

        // Assert
        Assert.Null(disagreement);
    }

    /// <summary>
    /// A fallback on a different geometry does not produce a degraded vector; it produces a point in another space,
    /// and a distance computed against it is a number with no meaning. The report has to say which property differs,
    /// because an operator told only that "the chain disagrees" diffs two blocks by hand to learn it.
    /// </summary>
    [Theory]
    [MemberData(nameof(DisagreeingChains))]
    public void FindDisagreement_EndpointsDifferingOnOneProperty_NamesBothAndTheProperty(
        EmbeddingEndpoint fallback,
        string expectedProperty)
    {
        // Arrange
        var chain = new[] { EmbeddingDeclarations.Endpoint("primary"), fallback };

        // Act
        var disagreement = EmbeddingChainAgreement.FindDisagreement(chain);

        // Assert
        Assert.NotNull(disagreement);
        Assert.Contains("primary", disagreement, StringComparison.Ordinal);
        Assert.Contains("fallback", disagreement, StringComparison.Ordinal);
        Assert.Contains(expectedProperty, disagreement, StringComparison.Ordinal);
    }

    public static TheoryData<EmbeddingEndpoint, string> DisagreeingChains() => new()
    {
        { EmbeddingDeclarations.Endpoint("fallback", provider: "other-vendor"), "provider" },
        { EmbeddingDeclarations.Endpoint("fallback", model: "text-embedding-3-large"), "model" },
        { EmbeddingDeclarations.Endpoint("fallback", modelVersion: "2026-01-01"), "model version" },
        { EmbeddingDeclarations.Endpoint("fallback", dimension: 8), "dimension" },
        {
            EmbeddingDeclarations.Endpoint("fallback", distanceMetric: EmbeddingDistanceMetric.InnerProduct),
            "distance metric"
        },
        { EmbeddingDeclarations.Endpoint("fallback", inputCharacterLimit: 4000), "input character limit" },
        { EmbeddingDeclarations.Endpoint("fallback", passageInstruction: "passage: "), "passage instructions" },
        { EmbeddingDeclarations.Endpoint("fallback", normalizesVector: false), "vector normalization" },
    };
}
