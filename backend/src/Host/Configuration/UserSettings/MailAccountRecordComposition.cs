// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Composes the record a user is served from out of their own document and the mail accounts assigned to them.</summary>
/// <remarks>
/// <para>
/// A mail account is a record of its own, and a user's document no longer carries one. What the binder judges and what
/// the roster publishes is still one record per user, because every rule about a user's mailboxes — that two of their
/// accounts cannot share a display name, that a junk destination resolves within their own accounts — is a rule over
/// the set that user is served. So the set is composed back in here, as the collection a document used to state, with the
/// generated identifier standing where an operator-typed one did.
/// </para>
/// <para>
/// An account holding no address is left out. An upgrade could not derive one for it, and serving a mailbox the
/// deployment cannot compare against every other would be serving the one kind of account the address exists to rule
/// out; the startup gate reports it instead.
/// </para>
/// </remarks>
internal static class MailAccountRecordComposition
{
    /// <summary>The property a composed record holds its accounts under.</summary>
    internal const string MailAccountsProperty = nameof(UserAccountOptions.MailAccounts);

    /// <summary>The property an account's generated identifier is composed under.</summary>
    internal const string AccountIdProperty = nameof(Mail.MailSynchronizationAccountOptions.AccountId);

    /// <summary>The property an account's display name is composed under.</summary>
    internal const string DisplayNameProperty = nameof(Mail.MailSynchronizationAccountOptions.DisplayName);

    /// <summary>The property a declaration states an account's address under, which is a column rather than a setting.</summary>
    internal const string EmailAddressProperty = "EmailAddress";

    /// <summary>Composes one user's served record.</summary>
    /// <param name="userJson">The user's own document.</param>
    /// <param name="accounts">The accounts assigned to the user.</param>
    /// <returns>The record the binder judges, or <paramref name="userJson" /> unchanged when it is not a JSON object, which the binder refuses on its own terms.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static string Compose(string userJson, IEnumerable<MailAccountRecord> accounts)
    {
        ArgumentNullException.ThrowIfNull(userJson);
        ArgumentNullException.ThrowIfNull(accounts);

        JsonObject record;

        try
        {
            if (JsonNode.Parse(userJson) is not JsonObject parsed)
            {
                return userJson;
            }

            record = parsed;
        }
        catch (JsonException)
        {
            return userJson;
        }

        RemoveEverySpelling(record, MailAccountsProperty);

        var served = accounts.Where(IsServed).ToArray();

        if (served.Length > 0)
        {
            var keyed = new JsonObject();

            foreach (var (position, account) in served.Index())
            {
                keyed[position.ToString(CultureInfo.InvariantCulture)] = DeclarationOf(account);
            }

            record[MailAccountsProperty] = keyed;
        }

        return record.ToJsonString();
    }

    /// <summary>Reports whether an account is one a user can be served, which is an account holding an address.</summary>
    /// <param name="account">The account.</param>
    /// <returns><see langword="true" /> when the account states an address.</returns>
    internal static bool IsServed(MailAccountRecord account) =>
        !string.IsNullOrWhiteSpace(account?.EmailAddress);

    /// <summary>Composes the declaration an account is bound as, its identifier and name standing beside its settings.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The declaration, or a JSON string standing in for a document that is not an object so the binder refuses it by name.</returns>
    internal static JsonNode DeclarationOf(MailAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        JsonObject declaration;

        try
        {
            if (JsonNode.Parse(account.Document) is not JsonObject parsed)
            {
                return JsonValue.Create(account.Document);
            }

            declaration = parsed;
        }
        catch (JsonException)
        {
            return JsonValue.Create(account.Document);
        }

        RemoveEverySpelling(declaration, AccountIdProperty);
        RemoveEverySpelling(declaration, DisplayNameProperty);
        RemoveEverySpelling(declaration, EmailAddressProperty);

        declaration[AccountIdProperty] = account.Id.ToString("D");
        declaration[DisplayNameProperty] = account.DisplayName;

        return declaration;
    }

    /// <summary>Removes a property however a document spelt it, which is how every configuration provider matches a key.</summary>
    internal static void RemoveEverySpelling(JsonObject parent, string property)
    {
        foreach (var spelling in parent
            .Select(entry => entry.Key)
            .Where(key => key.Equals(property, StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            parent.Remove(spelling);
        }
    }
}
