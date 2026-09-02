// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings;

/// <summary>Covers what an embedding failure publishes to a boundary and to the pipeline that decides about repeating.</summary>
public sealed class EmbeddingGenerationFailedExceptionTests
{
    [Theory]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected, 61001)]
    [InlineData(EmbeddingGenerationFailure.RateLimited, 62001)]
    [InlineData(EmbeddingGenerationFailure.RequestTimedOut, 62001)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted, 62001)]
    [InlineData(EmbeddingGenerationFailure.RequestRefused, 62001)]
    [InlineData(EmbeddingGenerationFailure.VectorShapeUnexpected, 63001)]
    public void ErrorCode_NamesWhatAnOperatorDoesAboutTheFailure(
        EmbeddingGenerationFailure failure,
        int expectedCode)
    {
        // Arrange
        var thrown = new EmbeddingGenerationFailedException("primary", failure);

        // Assert
        Assert.Equal(expectedCode, thrown.ErrorCode.Value);
        Assert.Contains(thrown.ErrorCode, MailFathomErrorCode.All);
    }

    /// <summary>
    /// Repeating a refused credential buys the same refusal while the account carries the request, and repeating a
    /// rejected request or an answer of the wrong shape needs a declaration to change first.
    /// </summary>
    [Theory]
    [InlineData(EmbeddingGenerationFailure.RateLimited, true)]
    [InlineData(EmbeddingGenerationFailure.RequestTimedOut, true)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted, true)]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected, false)]
    [InlineData(EmbeddingGenerationFailure.RequestRefused, false)]
    [InlineData(EmbeddingGenerationFailure.VectorShapeUnexpected, false)]
    public void IsWorthRepeating_SeparatesWhatAnotherAttemptCanChange(
        EmbeddingGenerationFailure failure,
        bool expected)
    {
        // Arrange
        var thrown = new EmbeddingGenerationFailedException("primary", failure);

        // Assert
        Assert.Equal(expected, thrown.IsWorthRepeating);
    }

    /// <summary>An endpoint address identifies a tenant and a resource, and this message reaches a log.</summary>
    [Fact]
    public void Message_NamesTheAliasTheOperatorChoseAndNoAddress()
    {
        // Arrange
        var thrown = new EmbeddingGenerationFailedException(
            "eu-primary",
            EmbeddingGenerationFailure.CredentialRejected,
            new InvalidOperationException("https://contoso.openai.azure.com/openai/v1/"));

        // Assert
        Assert.Contains("eu-primary", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("contoso", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }
}
