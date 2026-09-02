// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Resilience;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the adapter against a scripted provider, so every failure path is proved without a network.</summary>
public sealed class ProviderTextEmbeddingGeneratorTests
{
    private const int Dimension = EmbeddingDeclarations.Dimension;

    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    [Fact]
    public async Task GenerateAsync_AProviderAnsweringInTheDeclaredSpace_ReturnsOneVectorPerPassage()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 2));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan());

        // Act
        var vectors = await generator.GenerateAsync(["first", "second"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, vector => Assert.Equal(Dimension, vector.Dimension));
    }

    /// <summary>The narrower space is asked for, because a model trained to answer at a requested width already normalizes it.</summary>
    [Fact]
    public async Task GenerateAsync_AnEndpointHonouringARequestedWidth_AsksForTheDeclaredDimension()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan());

        // Act
        await generator.GenerateAsync(["first"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            $"\"dimensions\":{Dimension}",
            provider.LastRequestBody(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_AnEndpointThatDoesNotHonourARequestedWidth_AsksForNone()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(
            endpoints: [EmbeddingDeclarations.Endpoint(supportsRequestedDimension: false)]));

        // Act
        await generator.GenerateAsync(["first"], TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("dimensions", provider.LastRequestBody(), StringComparison.Ordinal);
    }

    /// <summary>Every endpoint of a chain reaches the same space, so a fallback's vectors belong to the same profile.</summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task GenerateAsync_AnEndpointThatFailed_FallsThroughToTheNext(HttpStatusCode status)
    {
        // Arrange
        using var provider = ScriptedProvider.Refusing(status).ThenAnswering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(
            endpoints:
            [
                EmbeddingDeclarations.Endpoint("primary"),
                EmbeddingDeclarations.Endpoint("fallback", address: "https://second.invalid/v1/"),
            ]));

        // Act
        var vectors = await generator.GenerateAsync(["first"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(vectors);
        Assert.Equal(2, provider.RequestCount);
    }

    /// <summary>
    /// An open circuit on the first endpoint is exactly the condition a fallback exists for, so the pipeline declining
    /// to call it has to reach the next one rather than end the request.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_AnEndpointItsPipelineDeclinedToCall_FallsThroughToTheNext()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(
            EmbeddingDeclarations.Plan(
                endpoints:
                [
                    EmbeddingDeclarations.Endpoint("primary"),
                    EmbeddingDeclarations.Endpoint("fallback", address: "https://second.invalid/v1/"),
                ]),
            declinedInstances: ["primary"]);

        // Act
        var vectors = await generator.GenerateAsync(["first"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(vectors);

        // The declined endpoint was never called, which is the whole point of a circuit that is already open.
        Assert.Equal(1, provider.RequestCount);
    }

    [Fact]
    public async Task GenerateAsync_EveryEndpointFailing_ReportsTheLastFailure()
    {
        // Arrange
        using var provider = ScriptedProvider.Refusing(HttpStatusCode.Unauthorized).ThenRefusing(HttpStatusCode.TooManyRequests);
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(
            endpoints:
            [
                EmbeddingDeclarations.Endpoint("primary"),
                EmbeddingDeclarations.Endpoint("fallback", address: "https://second.invalid/v1/"),
            ]));

        // Act
        var failure = await Assert.ThrowsAsync<EmbeddingGenerationFailedException>(() =>
            generator.GenerateAsync(["first"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.RateLimited, failure.Failure);
        Assert.Contains("fallback", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A width nothing declared means the declaration is wrong, and every endpoint of the chain declares the same one,
    /// so asking the next would buy a second paid call to learn the same thing.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_AWidthTheProfileDoesNotRecord_FailsWithoutTryingTheNextEndpoint()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension + 4, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(
            endpoints:
            [
                EmbeddingDeclarations.Endpoint("primary"),
                EmbeddingDeclarations.Endpoint("fallback", address: "https://second.invalid/v1/"),
            ]));

        // Act
        var failure = await Assert.ThrowsAsync<EmbeddingGenerationFailedException>(() =>
            generator.GenerateAsync(["first"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.VectorShapeUnexpected, failure.Failure);
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>With trimming allowed the vectors are cut to the declared width and renormalized, because a shortened unit vector is no longer one.</summary>
    [Fact]
    public async Task GenerateAsync_AWiderAnswerWithTrimmingAllowed_ShortensAndRenormalizes()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension + 4, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(allowTrimVectors: true));

        // Act
        var vectors = await generator.GenerateAsync(["first"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Dimension, vectors[0].Dimension);
        Assert.Equal(1d, Length(vectors[0]), 5);
    }

    /// <summary>Trimming never widens: an answer narrower than the declared space is not that space at all.</summary>
    [Fact]
    public async Task GenerateAsync_ANarrowerAnswerWithTrimmingAllowed_IsStillRefused()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension - 1, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(allowTrimVectors: true));

        // Act
        var failure = await Assert.ThrowsAsync<EmbeddingGenerationFailedException>(() =>
            generator.GenerateAsync(["first"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.VectorShapeUnexpected, failure.Failure);
    }

    /// <summary>
    /// Fewer vectors than passages leaves the caller mapping vectors onto the wrong chunks, which no later check can
    /// see because every vector is individually valid.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_FewerVectorsThanPassages_IsRefused()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan());

        // Act
        var failure = await Assert.ThrowsAsync<EmbeddingGenerationFailedException>(() =>
            generator.GenerateAsync(["first", "second"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.VectorShapeUnexpected, failure.Failure);
    }

    /// <summary>A transport fault never reaches a status, so it is classified from the failure alone.</summary>
    [Fact]
    public async Task GenerateAsync_AnEndpointThatCannotBeReached_IsATransportFault()
    {
        // Arrange
        using var provider = ScriptedProvider.Throwing(new HttpRequestException("no route to host"));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan());

        // Act
        var failure = await Assert.ThrowsAsync<EmbeddingGenerationFailedException>(() =>
            generator.GenerateAsync(["first"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.TransportFaulted, failure.Failure);
    }

    /// <summary>A caller that cancelled is not a provider that failed, and the distinction has to survive the adapter.</summary>
    [Fact]
    public async Task GenerateAsync_ACallerThatCancelled_IsNotAProviderFailure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var provider = ScriptedProvider.Throwing(new HttpRequestException("unreachable"));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan());

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            generator.GenerateAsync(["first"], cancellation.Token));
    }

    [Fact]
    public async Task GenerateAsync_MorePassagesThanTheDeclaredBatch_IsRefusedBeforeTheProviderSeesThem()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 3));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(maximumPassagesPerCall: 2));

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(["first", "second", "third"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A passage reaching a hosted endpoint leaves the deployment, so every one of a batch is scanned first.</summary>
    [Fact]
    public async Task GenerateAsync_ASwitchedOnScanner_RedactsEveryPassageBeforeTheRequestIsSent()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 2));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(), egressGuard: egress.Guard);

        // Act
        await generator.GenerateAsync(
            [$"the key is {Marker}", "nothing here"],
            TestContext.Current.CancellationToken);

        // Assert
        var sent = provider.LastRequestBody();

        Assert.DoesNotContain(Marker, sent, StringComparison.Ordinal);
        Assert.Contains("[redacted:CloudKey]", sent, StringComparison.Ordinal);
    }

    /// <summary>Embedding a passage nothing could scan would leave the deployment carrying whatever it held.</summary>
    [Fact]
    public async Task GenerateAsync_ADetectorThatCannotAnswer_RefusesTheCallWithoutReachingTheProvider()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan(), egressGuard: egress.Guard);

        // Act
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            generator.GenerateAsync(["first"], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>An opt-in nobody took must not appear on this path at all.</summary>
    [Fact]
    public async Task GenerateAsync_ADeploymentThatScansNothing_SendsEveryPassageAsItWasComposed()
    {
        // Arrange
        using var provider = ScriptedProvider.Answering(VectorsOfWidth(Dimension, 1));
        var generator = provider.GeneratorOver(EmbeddingDeclarations.Plan());

        // Act
        await generator.GenerateAsync([$"the key is {Marker}"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(Marker, provider.LastRequestBody(), StringComparison.Ordinal);
    }

    private static double Length(EmbeddingVector vector) =>
        Math.Sqrt(vector.Components.ToArray().Sum(component => (double)component * component));

    /// <summary>Stands in for whatever the resilience boundary raises when a configured limit stopped an operation.</summary>
    /// <remarks>
    /// A type of its own rather than the real one, and that is the point being proved rather than a convenience: the
    /// adapter recognizes this failure by its stable error code, because the exception that carries it belongs to
    /// another adapter boundary this one may not reference. A double carrying the same code is therefore
    /// indistinguishable to the code under test, and a test that used the real type would prove less.
    /// </remarks>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A test double for one behavior of one test class, which nothing outside it raises or catches.")]
    private sealed class PipelineDeclinedTheOperation()
        : MailFathomException("An outbound resilience limit stopped the operation.")
    {
        public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OutboundDependencyUnavailable;
    }

    /// <summary>Builds the embeddings payload a provider answers with, each vector already of unit length.</summary>
    private static string VectorsOfWidth(int width, int count)
    {
        var components = Enumerable
            .Range(0, width)
            .Select(_ => (1d / Math.Sqrt(width)).ToString("R", CultureInfo.InvariantCulture));
        var vector = string.Join(',', components);
        var entries = Enumerable
            .Range(0, count)
            .Select(index => $"{{\"object\":\"embedding\",\"index\":{index},\"embedding\":[{vector}]}}");

        return $"{{\"object\":\"list\",\"model\":\"text-embedding-3-small\",\"data\":[{string.Join(',', entries)}],"
            + "\"usage\":{\"prompt_tokens\":1,\"total_tokens\":1}}";
    }

    /// <summary>A provider that answers from a script, so the adapter is exercised over a real client and no network.</summary>
    private sealed class ScriptedProvider : IDisposable
    {
        private readonly List<Func<HttpRequestMessage, HttpResponseMessage>> script = [];
        private readonly List<string> requestBodies = [];
        private readonly FakeHttpMessageHandler handler;

        private int cursor;

        private ScriptedProvider() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.requestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return this.script[Math.Min(this.cursor++, this.script.Count - 1)](request);
            });

        public int RequestCount => this.requestBodies.Count;

        /// <summary>The health state every call this provider serves reports into, so a test can read what the run established.</summary>
        public IAiProviderHealthRecorder HealthRecorder { get; } = Substitute.For<IAiProviderHealthRecorder>();

        public static ScriptedProvider Answering(string payload) => new ScriptedProvider().ThenAnswering(payload);

        public static ScriptedProvider Refusing(HttpStatusCode status) => new ScriptedProvider().ThenRefusing(status);

        public static ScriptedProvider Throwing(Exception failure)
        {
            var provider = new ScriptedProvider();
            provider.script.Add(_ => throw failure);

            return provider;
        }

        public ScriptedProvider ThenAnswering(string payload)
        {
            this.script.Add(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });

            return this;
        }

        public ScriptedProvider ThenRefusing(HttpStatusCode status)
        {
            this.script.Add(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"error\":{\"message\":\"refused\"}}", Encoding.UTF8, "application/json"),
            });

            return this;
        }

        public string LastRequestBody() => this.requestBodies[^1];

        /// <summary>Composes the adapter over this provider, with the resilience budget passed straight through.</summary>
        /// <remarks>
        /// The runner is a pass-through rather than a real pipeline because what belongs to the pipeline — the retry
        /// bounds, the circuit, the concurrency limiter — is the infrastructure boundary's and is covered there. What
        /// this suite proves is the classification the pipeline reads, which has to be produced whether or not one is
        /// wrapped around it.
        /// </remarks>
        public ProviderTextEmbeddingGenerator GeneratorOver(
            EmbeddingGenerationPlan plan,
            IReadOnlyCollection<string>? declinedInstances = null,
            SensitiveContentEgressGuard? egressGuard = null)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(
                    ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<IReadOnlyList<Embedding<float>>>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    // A declined instance stands in for a pipeline that refused to call the endpoint at all — an open
                    // circuit or a spent concurrency budget — which is the failure the real executor translates into
                    // this type.
                    if (declinedInstances?.Contains(call.Arg<string>()) is true)
                    {
                        throw new PipelineDeclinedTheOperation();
                    }

                    // The substitute cannot know the argument is present, and it always is: this configuration matches
                    // the only overload the adapter calls.
                    var operation = call.Arg<Func<CancellationToken, Task<IReadOnlyList<Embedding<float>>>>>()!;

                    return operation(call.Arg<CancellationToken>());
                });

            return new ProviderTextEmbeddingGenerator(
                plan,
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                this.HealthRecorder,
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                NullLogger<ProviderTextEmbeddingGenerator>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
