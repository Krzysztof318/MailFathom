// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;

namespace MailFathom.Evaluations.Costing;

/// <summary>Reads what a chat completion cost out of the answer the provider sent, and records it against one meter.</summary>
/// <remarks>
/// <para>
/// The charge is the provider's rather than this repository's arithmetic: OpenRouter returns <c>usage.cost</c> on every
/// answer, in credits it has already billed, so a report composed from it states what the run actually spent instead of
/// what a price list committed here would have guessed a week after a provider moved its prices.
/// </para>
/// <para>
/// It reads the transport rather than the chat client because it has to sit beneath the run's response cache to be
/// honest, and the wire is the one place an answer the cache served never reaches. A provider that reports no cost — any
/// endpoint that is not OpenRouter — still has its calls and tokens counted, and the report says the charge is unstated.
/// </para>
/// </remarks>
/// <param name="meter">What the readings are recorded against.</param>
/// <param name="transport">The handler that actually sends the request.</param>
internal sealed class ProviderSpendHandler(SpendMeter meter, HttpMessageHandler transport) : DelegatingHandler(transport)
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType is "application/json")
        {
            // Buffering leaves the content readable, so the client still reads the same answer this does.
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            this.Record(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return response;
    }

    /// <summary>Reads what one answer cost, where the answer says.</summary>
    /// <remarks>
    /// A body that claims to be JSON and is not — a connection cut mid-answer, a proxy mislabelling a truncated one —
    /// leaves the charge unstated rather than failing the call. This handler observes a request it does not own, and the
    /// caller reading the same body is the one entitled to report it as broken.
    /// </remarks>
    private void Record(string answer)
    {
        try
        {
            using var document = JsonDocument.Parse(answer);

            if (!document.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind is not JsonValueKind.Object)
            {
                return;
            }

            meter.Record(
                Count(usage, "prompt_tokens"),
                Count(usage, "completion_tokens"),
                usage.TryGetProperty("cost", out var cost) && cost.TryGetDecimal(out var charge) ? charge : null);
        }
        catch (JsonException)
        {
            // Nothing is recorded, which is what an answer carrying no usage already reports.
        }
    }

    private static long Count(JsonElement usage, string property) =>
        usage.TryGetProperty(property, out var value) && value.TryGetInt64(out var count) ? count : 0;
}
