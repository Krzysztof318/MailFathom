// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.AI.Embeddings;

/// <summary>Produces vectors from the text alone, reaching no provider and costing nothing.</summary>
/// <remarks>
/// <para>
/// Almost everything this feature needs proved is a property of the schema, the worker, or the switch rather than of a
/// model: the dimension check, the uniqueness constraint, the per-profile index, the idempotent write, two generations
/// coexisting, the bounded removal. All of it is provable against a real database and this generator, which is why
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// requires it to exist: embedding is the first thing MailFathom does that costs money per unit of mail, and a
/// verification suite that spends the maintainer's credit on every run is one nobody can afford to run.
/// </para>
/// <para>
/// The vectors carry no meaning and this type never claims otherwise. Its identity names a provider of its own, so a
/// profile built on it can never be confused with one a model produced, and a deployment that activated it by accident
/// is visible in the profile row rather than in the quality of its search results. It is deterministic, which is the
/// one property downstream work needs: embedding a passage twice produces the same vector, so an idempotent write is
/// testable and a re-run changes nothing.
/// </para>
/// </remarks>
public sealed class DeterministicTextEmbeddingGenerator : ITextEmbeddingGenerator
{
    /// <summary>The provider name a profile built on this generator records.</summary>
    /// <remarks>Not a vendor, and deliberately unlike one: it is the record that these vectors came from a hash rather than a model.</remarks>
    public const string ProviderName = "mailfathom.deterministic";

    /// <summary>The model identifier a profile built on this generator records.</summary>
    /// <remarks>Versioned in the name, because changing how a component is derived changes every vector and must therefore be a different space rather than the same one with different contents.</remarks>
    public const string ModelName = "hashed-projection-v1";

    /// <summary>The greatest number of passages one call accepts.</summary>
    /// <remarks>Bounded like the provider adapter's, so a caller written against this generator meets the same limit when it is pointed at a real one.</remarks>
    public const int PassagesPerCall = 256;

    private const string HashDomain = "mailfathom.deterministic-embedding.v1";

    /// <summary>Initializes a generator producing vectors of one width.</summary>
    /// <param name="dimension">The width of the space, which the identity records.</param>
    /// <param name="inputCharacterLimit">The width a passage is cut to before it is hashed.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is not positive.</exception>
    public DeterministicTextEmbeddingGenerator(int dimension, int inputCharacterLimit) =>
        this.Identity = EmbeddingProfileIdentity.Create(
            ProviderName,
            ModelName,
            modelVersion: null,
            dimension,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(inputCharacterLimit, passageInstruction: null, normalizesVector: true));

    /// <inheritdoc />
    public EmbeddingProfileIdentity Identity { get; }

    /// <inheritdoc />
    public int MaximumPassagesPerCall => PassagesPerCall;

    /// <inheritdoc />
    /// <remarks>Completes synchronously, because nothing here waits on anything; the signature is the port's and the work is a hash.</remarks>
    public Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
    {
        EmbeddingRequestBounds.Require(passages, PassagesPerCall);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<EmbeddingVector> vectors =
        [
            .. passages.Select(passage => this.Project(
                EmbeddingPassagePreparation.Prepare(passage, this.Identity.InputPreparation))),
        ];

        return Task.FromResult(vectors);
    }

    /// <summary>Derives one vector by expanding a digest of the passage into components and normalizing the result.</summary>
    /// <remarks>
    /// The counter is part of every block's input rather than the block being chained, so the components depend on the
    /// passage and their own position and on nothing else — which is what makes the vector reproducible on any machine
    /// and independent of how the work was batched.
    /// </remarks>
    private EmbeddingVector Project(string passage)
    {
        var components = new float[this.Identity.Dimension];
        var seed = Encoding.UTF8.GetBytes(HashDomain + '\0' + passage);

        Span<byte> block = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> counter = stackalloc byte[sizeof(int)];

        for (var position = 0; position < components.Length;)
        {
            BinaryPrimitives.WriteInt32BigEndian(counter, position);

            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(counter);
            digest.AppendData(seed);
            digest.GetHashAndReset(block);

            for (var offset = 0; offset + sizeof(uint) <= block.Length && position < components.Length; offset += sizeof(uint))
            {
                // Centred on zero so the components spread over the whole space rather than one orthant, which is what
                // keeps two unrelated passages from landing close together under a cosine distance.
                var sample = BinaryPrimitives.ReadUInt32BigEndian(block[offset..]);
                components[position++] = (sample / (float)uint.MaxValue) - 0.5f;
            }
        }

        return EmbeddingVector.Create(components).Normalize();
    }
}
