// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.AppHost;

internal sealed class DevelopmentCredentialProvisioner(HttpClient client, TimeProvider timeProvider)
{
    private static readonly TimeSpan ReadinessRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    internal async Task<Guid> WaitForSoleServedUserAsync(
        Uri startedEndpoint,
        Uri adminEndpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startedEndpoint);
        ArgumentNullException.ThrowIfNull(adminEndpoint);

        await this.WaitForStartedAsync(startedEndpoint, cancellationToken);

        return await this.ReadSoleServedUserAsync(adminEndpoint, cancellationToken);
    }

    internal async Task<bool> EnsureCredentialAsync(
        Uri adminEndpoint,
        Guid user,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (await this.CredentialExistsAsync(adminEndpoint, user, username, cancellationToken))
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
            new Uri(adminEndpoint, $"api/admin/users/{user:D}/credentials"))
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

    /// <summary>Declares the mailbox this run collected into the served user's record, unless it is already there.</summary>
    /// <param name="adminEndpoint">The administrative surface the record is written through.</param>
    /// <param name="user">The user the mailbox belongs to.</param>
    /// <param name="host">The IMAP server the mailbox is read from.</param>
    /// <param name="userName">The login the mailbox is reached under.</param>
    /// <param name="password">The password that login is presented with.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><see langword="true" /> when this call declared the account, and <see langword="false" /> when the record already declared it.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deployment refused the declaration.</exception>
    /// <remarks>
    /// The password travels as a <c>plaintext:</c> reference rather than as stored material, which is what the
    /// environment variable this replaced carried and is the same decision it was: a developer's own mailbox password,
    /// on a loopback deployment, is not worth a rotation surface. A deployment anybody else reaches provisions it with
    /// <c>mfctl user secret</c> and names the reference that write hands back.
    /// </remarks>
    internal async Task<bool> EnsureMailAccountAsync(
        Uri adminEndpoint,
        Guid user,
        string host,
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var record = await this.ReadRecordAsync(adminEndpoint, user, cancellationToken);

        if (DeclaresDevelopmentAccount(record.Document))
        {
            return false;
        }

        var account = new JsonObject
        {
            ["AccountId"] = OrchestrationContract.DevelopmentMailAccountId,
            ["DisplayName"] = OrchestrationContract.DevelopmentMailAccountDisplayName,
            ["Host"] = host,
            ["UserName"] = userName,
            ["Secrets"] = new JsonObject
            {
                ["Password"] = new JsonObject
                {
                    ["Name"] = OrchestrationContract.DevelopmentMailAccountPasswordName,
                    ["SecretReference"] = $"plaintext:{password}",
                },
            },
        };

        var requestBody = new JsonObject
        {
            ["version"] = record.Version,
            ["account"] = account.ToJsonString(),
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(adminEndpoint, $"api/admin/users/{user:D}/record/mail-accounts"))
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        if (!document.RootElement.GetProperty("committed").GetBoolean())
        {
            throw new InvalidOperationException(
                $"The local MailFathom host refused the mailbox declaration: {RefusalOf(document.RootElement)}");
        }

        return true;
    }

    private static string RefusalOf(JsonElement outcome) => string.Join(
        " ",
        outcome.GetProperty("messages").EnumerateArray().Select(static message => message.GetString()));

    private static bool DeclaresDevelopmentAccount(string recordJson)
    {
        using var record = JsonDocument.Parse(recordJson);

        return record.RootElement.ValueKind == JsonValueKind.Object
            && record.RootElement.TryGetProperty("MailAccounts", out var accounts)
            && accounts.ValueKind == JsonValueKind.Array
            && accounts.EnumerateArray().Any(static account =>
                account.ValueKind == JsonValueKind.Object
                && account.TryGetProperty("AccountId", out var accountId)
                && string.Equals(
                    accountId.GetString(),
                    OrchestrationContract.DevelopmentMailAccountId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(long Version, string Document)> ReadRecordAsync(
        Uri adminEndpoint,
        Guid user,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, $"api/admin/users/{user:D}/record"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return (
            document.RootElement.GetProperty("version").GetInt64(),
            document.RootElement.GetProperty("document").GetString() ?? "{}");
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

    private async Task<Guid> ReadSoleServedUserAsync(Uri adminEndpoint, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, "api/admin/users"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var users = document.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Select(static user => user.GetProperty("id").GetGuid())
            .ToArray();

        return users.Length == 1
            ? users[0]
            : throw new InvalidOperationException(
                $"The normal Aspire launch expected one recorded user but found {users.Length.ToString(CultureInfo.InvariantCulture)}.");
    }

    private async Task<bool> CredentialExistsAsync(
        Uri adminEndpoint,
        Guid user,
        string username,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, $"api/admin/users/{user:D}/credentials"),
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
