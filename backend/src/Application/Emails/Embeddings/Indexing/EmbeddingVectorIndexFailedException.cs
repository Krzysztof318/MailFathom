// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.Embeddings.Indexing;

/// <summary>Indicates that the approximate index belonging to one embedding profile could not be built or removed.</summary>
/// <remarks>
/// <para>
/// An exception rather than a result, because the fact travels past code that cannot decide what it means: the database
/// refused a statement, and whether that ends an activation or merely leaves it slower is the operator command's call
/// rather than the store's. What it carries is what such a caller needs to report — which profile, and which of the two
/// operations — with the database's own words kept as the inner exception for a log.
/// </para>
/// <para>
/// The message names the profile by its identifier and nothing else. A profile is a model, a width, and a metric, so
/// none of it is personal data; a vector, a passage, and a message are absent by construction, because this operation
/// never reads one.
/// </para>
/// </remarks>
public sealed class EmbeddingVectorIndexFailedException : MailFathomException
{
    /// <summary>Initializes a new failure to bring one profile's approximate index into the state its lifecycle asked for.</summary>
    /// <param name="operatorSafeMessage">A message naming the profile and which operation was refused.</param>
    /// <param name="profileId">The profile whose index the refused statement was for.</param>
    /// <param name="innerException">The database failure that revealed it.</param>
    public EmbeddingVectorIndexFailedException(
        string operatorSafeMessage,
        EmbeddingProfileId profileId,
        Exception innerException)
        : base(operatorSafeMessage, innerException) => this.ProfileId = profileId;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EmbeddingVectorIndexUnavailable;

    /// <summary>Gets the profile whose approximate index is not in the state its lifecycle asked for.</summary>
    public EmbeddingProfileId ProfileId { get; }
}
