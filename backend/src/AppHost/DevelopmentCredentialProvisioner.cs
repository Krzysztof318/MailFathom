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
    /// <summary>The label the local launch records its user under where the database holds nobody.</summary>
    private const string RecordedUserDisplayName = "user";

    private static readonly TimeSpan ReadinessRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Waits for the local host to start, then names the one user it holds, recording them first where it holds nobody.</summary>
    /// <returns>That user.</returns>
    /// <remarks>
    /// The local launch is a quick start, so it records the user a fresh database lacks rather than leaving the developer a
    /// step before anything is provisioned — through the administrative API, as <c>mfctl user add</c> would. Nothing a
    /// deployment runs does this: a deployment's first user is the one its administrator records.
    /// </remarks>
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

    /// <summary>Creates the mailbox this run collected for the served user, unless an account for its address is already assigned to them.</summary>
    /// <param name="adminEndpoint">The administrative surface the account is written through.</param>
    /// <param name="user">The user the mailbox is assigned to.</param>
    /// <param name="host">The IMAP server the mailbox is read from.</param>
    /// <param name="userName">The login the mailbox is reached under.</param>
    /// <param name="password">The password that login is presented with.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><see langword="true" /> when this call created the account, and <see langword="false" /> when one was already assigned.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deployment refused the declaration.</exception>
    /// <remarks>
    /// <para>
    /// The password travels as a <c>plaintext:</c> reference rather than as stored material: a developer's own mailbox
    /// password, on a loopback deployment, is not worth a rotation surface.
    /// </para>
    /// <para>
    /// The address is the login where the login is one, which is what a mail provider ordinarily hands out, and is
    /// composed from the login and the server otherwise, so a local server keyed by bare user names still gets an account
    /// told apart from every other.
    /// </para>
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

        var emailAddress = userName.Contains('@', StringComparison.Ordinal) ? userName : $"{userName}@{host}";

        if (await this.AssignsAccountForAsync(adminEndpoint, user, emailAddress, cancellationToken))
        {
            return false;
        }

        var account = new JsonObject
        {
            ["EmailAddress"] = emailAddress,
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
            ["userId"] = user.ToString("D"),
            ["account"] = account.ToJsonString(),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(adminEndpoint, "api/admin/mail-accounts"))
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

    private static bool HoldsAddress(JsonElement account, string emailAddress) =>
        account.TryGetProperty("emailAddress", out var held)
        && string.Equals(held.GetString()?.Trim(), emailAddress, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> AssignsAccountForAsync(
        Uri adminEndpoint,
        Guid user,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, "api/admin/mail-accounts"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement
            .GetProperty("accounts")
            .EnumerateArray()
            .Any(account =>
                account.GetProperty("users").EnumerateArray().Any(assigned => assigned.GetGuid() == user)
                && HoldsAddress(account, emailAddress));
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

        return users switch
        {
            [] => await this.RecordUserAsync(adminEndpoint, cancellationToken),
            [var only] => only,
            _ => throw new InvalidOperationException(
                $"The normal Aspire launch expected one recorded user but found {users.Length.ToString(CultureInfo.InvariantCulture)}."),
        };
    }

    private async Task<Guid> RecordUserAsync(Uri adminEndpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(adminEndpoint, "api/admin/users"))
        {
            Content = new StringContent(
                new JsonObject { ["displayName"] = RecordedUserDisplayName }.ToJsonString(),
                Encoding.UTF8,
                "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement.GetProperty("id").GetGuid();
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
