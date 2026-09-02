// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.AppHost;

internal sealed class DevelopmentCredentialProvisioner(HttpClient client, TimeProvider timeProvider)
{
    private static readonly TimeSpan ReadinessRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    internal async Task<bool> EnsureAsync(
        Uri startedEndpoint,
        Uri adminEndpoint,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startedEndpoint);
        ArgumentNullException.ThrowIfNull(adminEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await this.WaitForStartedAsync(startedEndpoint, cancellationToken);

        var owner = await this.ReadSoleServedOwnerAsync(adminEndpoint, cancellationToken);
        if (await this.CredentialExistsAsync(adminEndpoint, owner, username, cancellationToken))
        {
            return false;
        }

        var requestBody = new JsonObject
        {
            ["method"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["permissions"] = null,
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(adminEndpoint, $"api/admin/owners/{owner:D}/credentials"))
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return true;
    }

    private async Task WaitForStartedAsync(Uri startedEndpoint, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + ReadinessTimeout;

        while (true)
        {
            try
            {
                using var response = await client.GetAsync(
                    startedEndpoint,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (timeProvider.GetUtcNow() >= deadline)
            {
                throw new TimeoutException("The local MailFathom host did not report startup readiness within two minutes.");
            }

            await Task.Delay(ReadinessRetryDelay, timeProvider, cancellationToken);
        }
    }

    private async Task<Guid> ReadSoleServedOwnerAsync(Uri adminEndpoint, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, "api/admin/owners"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var owners = document.RootElement
            .GetProperty("owners")
            .EnumerateArray()
            .Where(static owner => owner.GetProperty("served").GetBoolean())
            .Select(static owner => owner.GetProperty("id").GetGuid())
            .ToArray();

        return owners.Length == 1
            ? owners[0]
            : throw new InvalidOperationException(
                $"The normal Aspire launch expected one served owner but found {owners.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
    }

    private async Task<bool> CredentialExistsAsync(
        Uri adminEndpoint,
        Guid owner,
        string username,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, $"api/admin/owners/{owner:D}/credentials"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement
            .GetProperty("credentials")
            .EnumerateArray()
            .Any(credential =>
                string.Equals(credential.GetProperty("method").GetString(), "password", StringComparison.Ordinal)
                && string.Equals(credential.GetProperty("lookup").GetString(), username, StringComparison.OrdinalIgnoreCase));
    }
}
