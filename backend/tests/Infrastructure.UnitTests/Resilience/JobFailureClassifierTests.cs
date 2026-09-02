// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Jobs;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Resilience;

public sealed class JobFailureClassifierTests
{
    private readonly JobFailureClassifier classifier = new();

    /// <summary>
    /// A pipeline that declined the work says the dependency is unusable right now, which is the clearest statement a
    /// later attempt could succeed — and by the time one reaches a handler, the operation's own retry budget is spent,
    /// so the job's attempt is the next layer out rather than a second retry at the same one.
    /// </summary>
    [Fact]
    public void Classify_ADependencyThatDeclinedTheWork_IsTransient()
    {
        // Arrange
        var failure = new OutboundDependencyUnavailableException(
            OutboundDependency.AiProviderInvocation,
            new BrokenCircuitException());

        // Act
        var record = this.classifier.Classify(failure);

        // Assert
        Assert.Equal(JobFailureClassification.Transient, record.Classification);
    }

    /// <summary>A queue at its depth clears on its own, so the segment it refused is worth enqueuing again.</summary>
    [Fact]
    public void Classify_AHandOnTheQueueRefusedAtItsDepth_IsTransient()
    {
        // Arrange
        var failure = new JobHandOnRefusedAtCapacityException(JobType.ReclaimContentObjects);

        // Act
        var record = this.classifier.Classify(failure);

        // Assert
        Assert.Equal(JobFailureClassification.Transient, record.Classification);
    }

    /// <summary>An adapter that has already classified its provider's answer is deferred to, so the two never disagree.</summary>
    [Theory]
    [InlineData(EmbeddingGenerationFailure.RateLimited, JobFailureClassification.Transient)]
    [InlineData(EmbeddingGenerationFailure.TransportFaulted, JobFailureClassification.Transient)]
    [InlineData(EmbeddingGenerationFailure.CredentialRejected, JobFailureClassification.Permanent)]
    [InlineData(EmbeddingGenerationFailure.VectorShapeUnexpected, JobFailureClassification.Permanent)]
    public void Classify_AFailureThatDeclaresWhetherItIsWorthRepeating_TakesTheAnswerFromIt(
        EmbeddingGenerationFailure providerFailure,
        JobFailureClassification expected)
    {
        // Arrange
        var failure = new EmbeddingGenerationFailedException("embeddings", providerFailure);

        // Act
        var record = this.classifier.Classify(failure);

        // Assert
        Assert.Equal(expected, record.Classification);
    }

    /// <summary>A connection that dropped or a stream that ended means the same thing whichever dependency produced it.</summary>
    [Fact]
    public void Classify_ATransportFailure_IsTransient()
    {
        // Act
        var record = this.classifier.Classify(new SocketException());

        // Assert
        Assert.Equal(JobFailureClassification.Transient, record.Classification);
    }

    /// <summary>An adapter wraps what it caught, so the verdict has to be read off the chain rather than off the outermost type.</summary>
    [Fact]
    public void Classify_ARecognizedFailureBehindAWrapper_IsClassifiedFromTheCause()
    {
        // Arrange
        var failure = new InvalidOperationException(
            "the handler could not finish",
            new OutboundDependencyUnavailableException(
                OutboundDependency.DatabaseCommandExecution,
                new BrokenCircuitException()));

        // Act
        var record = this.classifier.Classify(failure);

        // Assert
        Assert.Equal(JobFailureClassification.Transient, record.Classification);
    }

    /// <summary>
    /// A failure whose meaning is unknown is not repeated, which is the same refusal the outbound classifier makes: a
    /// job that stops on its first attempt is visible as a dead letter, while the opposite mistake spends a whole
    /// budget repeating something nothing can fix.
    /// </summary>
    [Fact]
    public void Classify_AnUnrecognizedFailure_IsPermanent()
    {
        // Act
        var record = this.classifier.Classify(new InvalidOperationException("the handler could not finish"));

        // Assert
        Assert.Equal(JobFailureClassification.Permanent, record.Classification);
    }

    /// <summary>
    /// The reason is stored on the row and read back into every report of the job, and a handler works on mail — so a
    /// library's message, which may quote a subject or an address, never becomes one.
    /// </summary>
    [Fact]
    public void Classify_AFailureWhoseMessageQuotesTheMail_NamesTheTypeRatherThanTheMessage()
    {
        // Arrange
        var failure = new InvalidOperationException("Re: your invoice from alex@example.test could not be parsed");

        // Act
        var record = this.classifier.Classify(failure);

        // Assert
        Assert.Equal(nameof(InvalidOperationException), record.Reason);
        Assert.DoesNotContain("example.test", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failure MailFathom raised itself has a code an operator can look up, and it is the first one in the chain
    /// rather than the outermost wrapper, which usually names nothing.
    /// </summary>
    [Fact]
    public void Classify_AFirstPartyFailure_NamesItsTypeAndItsStableCode()
    {
        // Arrange
        var failure = new InvalidOperationException(
            "the handler could not finish",
            new OutboundDependencyUnavailableException(
                OutboundDependency.MailboxDataRetrieval,
                new BrokenCircuitException()));

        // Act
        var record = this.classifier.Classify(failure);

        // Assert
        Assert.Equal(
            $"{nameof(OutboundDependencyUnavailableException)} (41001)",
            record.Reason);
    }
}
