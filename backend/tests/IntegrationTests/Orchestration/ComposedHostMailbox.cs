// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.AppHost;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Declares the mailbox the composed host serves, in the record of the one user that host holds.</summary>
/// <remarks>
/// <para>
/// A deployment reads no mail account from its own file, so the app model cannot configure this one: every account a
/// host serves belongs to a user's record. The composed host starts holding the one user a fresh database is seeded
/// with and no mailbox at all, and this is what puts one there — through the administrative surface that host serves,
/// which is the same act an operator performs with <c>mfctl user account add</c>.
/// </para>
/// <para>
/// It runs after the host is healthy rather than before it starts, and that is the arrangement rather than a
/// concession: a record committed through the running host is published to its roster in the same write, so the
/// mailbox is served without a restart. A row written into the database from here would reach a process that never
/// asked the question again.
/// </para>
/// <para>
/// The account states an IMAP server it never reaches. Synchronization is switched off on this host, so no supervisor
/// opens a session, but a recorded mailbox is judged as one that will be synchronized — it names a server, a login,
/// and a credential or it is refused. The name is the reserved testing domain the submission endpoint uses, so nothing
/// resolves it even if something tried.
/// </para>
/// </remarks>
internal static class ComposedHostMailbox
{
    internal static async Task RecordAsync(Uri adminAddress, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = adminAddress };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", OrchestrationContract.AdminApiKey);

        var user = await ReadSoleUserAsync(client, cancellationToken);
        var version = await ReadRecordVersionAsync(client, user, cancellationToken);

        var requestBody = new JsonObject
        {
            ["version"] = version,
            ["account"] = Declaration().ToJsonString(),
        };
        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            new Uri($"api/admin/users/{user:D}/record/mail-accounts", UriKind.Relative),
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var outcome = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        if (!outcome.RootElement.GetProperty("committed").GetBoolean())
        {
            throw new InvalidOperationException(
                $"The composed host refused the mailbox declaration [{string.Join(
                    " ",
                    outcome.RootElement.GetProperty("messages").EnumerateArray().Select(static message => message.GetString()))}].");
        }
    }

    /// <summary>Composes the account exactly as a configuration file once stated it, which is the shape a record keeps.</summary>
    private static JsonObject Declaration() => new()
    {
        ["AccountId"] = OrchestrationContract.ServedMailAccountId,
        ["DisplayName"] = OrchestrationContract.ServedMailAccountDisplayName,
        ["Host"] = OrchestrationContract.ComposedHostSubmissionHost,
        ["UserName"] = OrchestrationContract.ComposedHostSendingAddress,
        ["Secrets"] = PasswordBlock(OrchestrationContract.ComposedHostReadingPasswordName),
        ["Folders"] = new JsonArray
        {
            new JsonObject
            {
                ["Alias"] = OrchestrationContract.ComposedHostReadableFolderAlias,
                ["RemotePath"] = OrchestrationContract.ComposedHostReadableFolderAlias,
            },
        },
        ["Delivery"] = new JsonObject
        {
            ["Enabled"] = true,
            ["Host"] = OrchestrationContract.ComposedHostSubmissionHost,
            ["FromAddress"] = OrchestrationContract.ComposedHostSendingAddress,
            ["Secrets"] = PasswordBlock(OrchestrationContract.ComposedHostSubmissionPasswordName),
        },
    };

    /// <summary>Composes one secret block, under the name it is declared by.</summary>
    /// <remarks>
    /// The name is the caller's rather than a constant here, because the reading block and the delivery block carry
    /// the same material under two names: a record is judged in one walk and a secret name is claimed once within it.
    /// </remarks>
    private static JsonObject PasswordBlock(string name) => new()
    {
        ["Password"] = new JsonObject
        {
            ["Name"] = name,
            ["SecretReference"] = $"plaintext:{OrchestrationContract.MailServerAccountPassword}",
        },
    };

    private static async Task<Guid> ReadSoleUserAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri("api/admin/users", UriKind.Relative),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var roster = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var users = roster.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Select(static user => user.GetProperty("id").GetGuid())
            .ToArray();

        return users.Length == 1
            ? users[0]
            : throw new InvalidOperationException(
                $"The composed host holds {users.Length} users, and the mailbox this suite reads belongs to one.");
    }

    private static async Task<long> ReadRecordVersionAsync(
        HttpClient client,
        Guid user,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri($"api/admin/users/{user:D}/record", UriKind.Relative),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var record = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return record.RootElement.GetProperty("version").GetInt64();
    }
}
