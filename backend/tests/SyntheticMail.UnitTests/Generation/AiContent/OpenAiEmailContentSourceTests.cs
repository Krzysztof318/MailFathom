// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Sockets;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Generation.AiContent;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation.AiContent;

/// <summary>What a provider failure is read as, and what an answer has to be to count as one.</summary>
/// <remarks>
/// The mapping is read from the failure's type and its status alone, for the reason the service's provider
/// classification gives, and the answer is held to the shape it was asked for: a run that printed a stack trace
/// where a developer needed a move, or built a corpus around an answer the model did not give, would have taken the
/// one thing this tool's failures exist to keep out of a developer's hands.
/// </remarks>
public sealed class OpenAiEmailContentSourceTests
{
    private static readonly AiProviderConfiguration Provider = new("not-a-real-key", "gpt-test", null);

    [Theory]
    [InlineData(401, "refused the API key")]
    [InlineData(403, "refused the API key")]
    [InlineData(404, "does not serve model")]
    [InlineData(429, "rate-limiting")]
    [InlineData(408, "timed out")]
    [InlineData(504, "timed out")]
    [InlineData(500, "failing its own requests")]
    [InlineData(503, "failing its own requests")]
    [InlineData(400, "refused the request")]
    [InlineData(0, "could not be reached")]
    public void ToFailure_AProviderRefusal_ReadsTheMoveFromTheStatusAlone(int status, string expected)
    {
        // Arrange
        using var response = new FakeClientResponse(status);
        var failure = new ClientResultException("refused", response);

        // Act
        var reported = OpenAiEmailContentSource.ToFailure(failure, Provider);

        // Assert
        Assert.Contains(expected, reported.Message, StringComparison.Ordinal);
        Assert.Same(failure, reported.InnerException);
    }

    [Fact]
    public void ToFailure_ANUnknownModel_IsNamedInTheMessage()
    {
        // Arrange
        using var response = new FakeClientResponse(404);
        var failure = new ClientResultException("refused", response);

        // Act
        var reported = OpenAiEmailContentSource.ToFailure(failure, Provider);

        // Assert
        Assert.Contains(Provider.Model, reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToFailure_ATransportFailure_IsARunThatCannotReachItsEndpoint()
    {
        // Arrange
        var failure = new SocketException((int)SocketError.ConnectionRefused);

        // Act
        var reported = OpenAiEmailContentSource.ToFailure(failure, Provider);

        // Assert
        Assert.Contains("could not be reached", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToFailure_ATimeout_IsARunThatWaitedTooLong()
    {
        // Arrange, Act
        var reported = OpenAiEmailContentSource.ToFailure(new TimeoutException(), Provider);

        // Assert
        Assert.Contains("timed out", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseContent_AnAnswerInTheShapeItWasAskedFor_ReadsSubjectAndBody()
    {
        // Arrange, Act
        var content = OpenAiEmailContentSource.ParseContent("""{ "subject": "  Figures  ", "body": "  Hello.\n\nMore. " }""");

        // Assert
        Assert.Equal("Figures", content.Subject);
        Assert.Equal("Hello.\n\nMore.", content.Body);
    }

    [Theory]
    [InlineData("certainly not json")]
    [InlineData("""{ "subject": "Figures" }""")]
    [InlineData("""{ "body": "Hello." }""")]
    [InlineData("""{ "subject": "  ", "body": "  " }""")]
    [InlineData("""{ "subject": "\u0000\u001b", "body": "Hello." }""")]
    public void ParseContent_AnAnswerThatIsNotOne_IsRefusedAsARetry(string contents)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => OpenAiEmailContentSource.ParseContent(contents));

        // Assert
        // A retry is the move rather than a corpus built around an answer the model did not give.
        Assert.Contains("Retry", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseContent_AnAnswerCarryingControlCharacters_IsReducedToWhatItsDestinationsCarry()
    {
        // Arrange, Act
        var content = OpenAiEmailContentSource.ParseContent(
            """{ "subject": "Figures\r\nSubject: injected", "body": "Hello.\u0000\n\nMore.\t" }""");

        // Assert
        // A line break in the subject would end the composed header early and make the rest of the answer a header,
        // so it is removed rather than carried; the body's own line breaks are its structure and stay.
        Assert.Equal("FiguresSubject: injected", content.Subject);
        Assert.Equal("Hello.\n\nMore.", content.Body);
    }

    /// <summary>The one member of a provider response the mapping reads, over a type with no public constructor.</summary>
    private sealed class FakeClientResponse(int status) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.Empty;

        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BinaryData.Empty);

        public override void Dispose()
        {
        }
    }
}
