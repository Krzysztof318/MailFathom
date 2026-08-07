// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Indexing;

/// <summary>Covers what a failed index operation publishes to the command that has to report it.</summary>
public sealed class EmbeddingVectorIndexFailedExceptionTests
{
    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0198f3d2-4b6a-7c1e-9f04-2a5b8c7d6e10"));

    /// <summary>
    /// One allocated code, in the persistence category, so a boundary reports the failure without recognizing the type.
    /// </summary>
    [Fact]
    public void ErrorCode_IsTheAllocatedVectorIndexCode()
    {
        // Arrange
        var thrown = FailureOf("The approximate vector index could not be built.");

        // Assert
        Assert.Equal(33001, thrown.ErrorCode.Value);
        Assert.Contains(thrown.ErrorCode, MailFathomErrorCode.All);
    }

    /// <summary>
    /// The profile travels with the failure so a caller names which generation is unindexed, and the database's own
    /// words stay in the inner exception, where a log reads them and a boundary does not.
    /// </summary>
    [Fact]
    public void Failure_CarriesTheProfileAndKeepsTheDatabaseWordsOutOfTheMessage()
    {
        // Arrange
        var refusal = new InvalidOperationException("permission denied for table email_embeddings at db.internal");

        // Act
        var thrown = new EmbeddingVectorIndexFailedException(
            $"The approximate vector index for embedding profile {ProfileId} could not be built.",
            ProfileId,
            refusal);

        // Assert
        Assert.Equal(ProfileId, thrown.ProfileId);
        Assert.Same(refusal, thrown.InnerException);
        Assert.DoesNotContain("db.internal", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static EmbeddingVectorIndexFailedException FailureOf(string operatorSafeMessage) =>
        new(operatorSafeMessage, ProfileId, new InvalidOperationException("refused"));
}
