// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Evaluations.Costing;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Providers;

/// <summary>A provider's embedding generator opened the way a deployment opens one, holding the transport and credential it speaks with.</summary>
/// <remarks>
/// The factory a deployment uses, for the reason <see cref="ProviderChatClient" /> gives, and without the resilience and
/// health decorators for the same reason.
/// </remarks>
internal sealed class ProviderEmbeddingGenerator : DelegatingEmbeddingGenerator<string, Embedding<float>>
{
    /// <summary>How long one request may take, sized for a batch of passages rather than one chat answer.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(1);

    private readonly HttpClient transport;
    private readonly ProviderEndpointCredential credential;

    private ProviderEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> providerGenerator,
        HttpClient transport,
        ProviderEndpointCredential credential)
        : base(providerGenerator)
    {
        this.transport = transport;
        this.credential = credential;
    }

    /// <summary>Opens a generator over one model.</summary>
    /// <param name="model">The model, and where it is reached.</param>
    /// <param name="apiKey">The key the provider authenticates the requests with.</param>
    /// <param name="spend">What every answer's tokens and charge are recorded against.</param>
    /// <returns>The generator, which the caller disposes.</returns>
    public static ProviderEmbeddingGenerator Open(EmbeddingModelUnderTest model, string apiKey, SpendMeter spend)
    {
        ArgumentNullException.ThrowIfNull(model);

        var credential = ProviderEndpointCredential.FromApiKey(apiKey, resolvedMaterial: null);
        var transport = ProviderSpendHandler.MeteredTransport(spend, RequestTimeout);

        try
        {
            return new ProviderEmbeddingGenerator(
                new OpenAiCompatibleClientFactory().OpenEmbeddingGenerator(EndpointFor(model), credential, transport),
                transport,
                credential);
        }
        catch
        {
            transport.Dispose();
            credential.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.transport.Dispose();
            this.credential.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Describes the model as the endpoint the factory opens.</summary>
    /// <remarks>
    /// The factory reads the alias, the address, and the routed name, and never the profile: the width a request asks for
    /// is an option of the request, which the scenario sets. So a run keeping each model's own width, which it cannot know
    /// before the first answer, declares one here that nothing reads.
    /// </remarks>
    private static EmbeddingEndpoint EndpointFor(EmbeddingModelUnderTest model) =>
        new(
            "evaluation",
            EmbeddingProfileIdentity.Create(
                "evaluation",
                model.RoutedModelName,
                modelVersion: null,
                model.Dimension ?? 1,
                EmbeddingDistanceMetric.Cosine,
                EmbeddingModelUnderTest.Preparation),
            model.Address,
            model.RoutedModelName,
            SupportsRequestedDimension: model.Dimension is not null);
}
