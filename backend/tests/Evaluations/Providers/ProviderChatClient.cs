// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Evaluations.Costing;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Providers;

/// <summary>A provider's chat client opened the way a deployment opens one, holding the transport and credential it speaks with.</summary>
/// <remarks>
/// The factory a deployment uses rather than a client built here, so what a scenario measures is the request a
/// deployment sends. What it leaves out is the resilience and budget decorators: a scenario is not a deployment's spend,
/// and a retried refusal would be several paid calls of the same answer. What it adds instead is
/// <see cref="TransientProviderRetryChatClient" />, which asks again only where the provider failed transiently, so every
/// model under test and the judge, all of which are opened here, ride out a rate limit the same way.
/// </remarks>
internal sealed class ProviderChatClient : DelegatingChatClient
{
    private readonly HttpClient transport;
    private readonly ProviderEndpointCredential credential;

    private ProviderChatClient(IChatClient providerClient, HttpClient transport, ProviderEndpointCredential credential)
        : base(providerClient)
    {
        this.transport = transport;
        this.credential = credential;
    }

    /// <summary>Opens a client over one endpoint.</summary>
    /// <param name="endpoint">Where requests go and which model they are routed to.</param>
    /// <param name="apiKey">The key the provider authenticates the requests with.</param>
    /// <param name="requestTimeout">How long one request may take before it is abandoned.</param>
    /// <param name="spend">What every answer's tokens and charge are recorded against.</param>
    /// <returns>The client, which the caller disposes.</returns>
    public static ProviderChatClient Open(
        ChatEndpoint endpoint,
        string apiKey,
        TimeSpan requestTimeout,
        SpendMeter spend)
    {
        var credential = ProviderEndpointCredential.FromApiKey(apiKey, resolvedMaterial: null);
        var transport = ProviderSpendHandler.MeteredTransport(spend, requestTimeout);

        try
        {
            return new ProviderChatClient(
                new OpenAiCompatibleClientFactory().OpenChatClient(endpoint, credential, transport)
                    .AsBuilder()
                    .Use(static inner => new TransientProviderRetryChatClient(inner, TimeProvider.System))
                    .Build(),
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
}
