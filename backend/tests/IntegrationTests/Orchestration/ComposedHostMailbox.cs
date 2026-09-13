// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.AppHost;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Adds the mailbox the composed host serves, assigned to the one user that host holds.</summary>
/// <remarks>
/// <para>
/// A deployment reads no mail account from its own file, so the app model cannot configure this one: every account a
/// host serves is a record of its own, assigned to the users it serves. The composed host may start holding nobody — a
/// fresh database records no user — and no mailbox at all, and this is what puts both there: through the administrative
/// surface that host serves, which is the same act an operator performs with <c>mfctl user add</c> and
/// <c>mfctl account add</c>.
/// </para>
/// <para>
/// It runs after the host is healthy rather than before it starts, and that is the arrangement rather than a
/// concession: an account committed through the running host is published to its roster in the same write, so the
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
    /// <summary>
    /// The label the suite records its user under where no earlier start over this database recorded one — the one
    /// <see cref="OrchestratedMailFathomServices" /> records under, so both converge on one row.
    /// </summary>
    private const string RecordedUserDisplayName = "user";

    internal static async Task RecordAsync(Uri adminAddress, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = adminAddress };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", OrchestrationContract.AdminApiKey);

        var user = await ReadOrRecordSoleUserAsync(client, cancellationToken);

        var requestBody = new JsonObject
        {
            ["userId"] = user.ToString("D"),
            ["account"] = Declaration().ToJsonString(),
        };
        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            new Uri("api/admin/mail-accounts", UriKind.Relative),
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

    /// <summary>Composes the account exactly as a configuration file once stated it, with the address that tells it apart.</summary>
    private static JsonObject Declaration() => new()
    {
        ["EmailAddress"] = OrchestrationContract.ComposedHostSendingAddress,
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

    private static async Task<Guid> ReadOrRecordSoleUserAsync(HttpClient client, CancellationToken cancellationToken)
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

        return users switch
        {
            [var only] => only,
            [] => await RecordUserAsync(client, cancellationToken),
            _ => throw new InvalidOperationException(
                $"The composed host holds {users.Length} users, and the mailbox this suite reads belongs to one."),
        };
    }

    private static async Task<Guid> RecordUserAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            new JsonObject { ["displayName"] = RecordedUserDisplayName }.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(new Uri("api/admin/users", UriKind.Relative), content, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var provisioned = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return provisioned.RootElement.GetProperty("id").GetGuid();
    }
}
