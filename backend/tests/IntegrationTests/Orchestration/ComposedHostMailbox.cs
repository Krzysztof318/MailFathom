// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.AppHost;
using MailFathom.Domain.Accounts;

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

    /// <summary>Records the mailbox, or finds the one an earlier start over this database recorded, and reports it with the user it belongs to.</summary>
    /// <remarks>
    /// The identifier is the one the deployment generated, so it is read back rather than stated: a composed-host test
    /// names the mailbox by it and seeds mail under it. An address is held by one account in the whole deployment, so an
    /// account already assigned to the user for this address is reused rather than created again and refused. The user
    /// comes back beside it because the credential a test authenticates with is provisioned for that user, and there is
    /// nowhere else the identifier is known.
    /// </remarks>
    internal static async Task<(MailAccountId Account, Guid User)> RecordAsync(
        Uri adminAddress,
        CancellationToken cancellationToken)
    {
        using var client = ComposedHostAdministration.Open(adminAddress);

        var user = await ReadOrRecordSoleUserAsync(client, cancellationToken);

        if (await FindAssignedAccountAsync(client, user, cancellationToken) is { } recorded)
        {
            return (recorded, user);
        }

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

        return (MailAccountId.Create(outcome.RootElement.GetProperty("accountId").GetGuid().ToString("D")), user);
    }

    private static async Task<MailAccountId?> FindAssignedAccountAsync(
        HttpClient client,
        Guid user,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri("api/admin/mail-accounts", UriKind.Relative),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var listing = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return listing.RootElement
            .GetProperty("accounts")
            .EnumerateArray()
            .Where(account => account.GetProperty("users").EnumerateArray().Any(assigned => assigned.GetGuid() == user)
                && string.Equals(
                    account.GetProperty("emailAddress").GetString(),
                    OrchestrationContract.ComposedHostSendingAddress,
                    StringComparison.OrdinalIgnoreCase))
            .Select(static account => (MailAccountId?)MailAccountId.Create(account.GetProperty("id").GetGuid().ToString("D")))
            .FirstOrDefault();
    }

    /// <summary>Composes the account exactly as a configuration file once stated it, with the address that tells it apart.</summary>
    private static JsonObject Declaration() => new()
    {
        ["EmailAddress"] = OrchestrationContract.ComposedHostSendingAddress,
        ["DisplayName"] = OrchestrationContract.ServedMailAccountDisplayName,
        // Stated by every account being written, because a derivation about this mail comes out in some language
        // whether or not anybody chose one. Nothing here reads a derivation, so the value is the shipped default
        // rather than a choice this suite is making.
        ["Language"] = "English",
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
    /// <para>
    /// The name is the caller's rather than a constant here, because the reading block and the delivery block carry
    /// the same material under two names: a record is judged in one walk and a secret name is claimed once within it.
    /// </para>
    /// <para>
    /// The reference names where the material is kept rather than carrying it. A record persisted with a
    /// <c>plaintext:</c> reference is refused outright — material in the column a user's declarations live in is the
    /// one outcome that check exists to prevent — so the app model hands the password to the host in
    /// <see cref="OrchestrationContract.ComposedHostMailboxPasswordVariable" /> and the record names that variable.
    /// </para>
    /// </remarks>
    private static JsonObject PasswordBlock(string name) => new()
    {
        ["Password"] = new JsonObject
        {
            ["Name"] = name,
            ["SecretReference"] = $"env:{OrchestrationContract.ComposedHostMailboxPasswordVariable}",
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
            [] => await ComposedHostAdministration.RecordUserAsync(client, RecordedUserDisplayName, cancellationToken),
            _ => throw new InvalidOperationException(
                $"The composed host holds {users.Length} users, and the mailbox this suite reads belongs to one."),
        };
    }
}
