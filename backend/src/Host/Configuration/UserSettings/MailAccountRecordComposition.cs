// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Composes the record a user is served from out of their own document and the mail accounts assigned to them.</summary>
/// <remarks>
/// <para>
/// A mail account is a record of its own, and a user's document no longer carries one. What the binder judges and what
/// a user is served from is still one record per user, because every rule about a user's mailboxes — that two of their
/// accounts cannot share a display name, that a junk destination resolves within their own accounts — is a rule over
/// the set that user is served. So the set is composed back in here, as the collection a document used to state, with the
/// generated identifier standing where an operator-typed one did.
/// </para>
/// <para>
/// An account holding no address is left out. An upgrade could not derive one for it, and serving a mailbox the
/// deployment cannot compare against every other would be serving the one kind of account the address exists to rule
/// out; composing the user reports it instead.
/// </para>
/// <para>
/// An account is composed as the settings policies governing it resolve it rather than as its row holds it, through
/// the policies read beside the account. That is the one place the resolution is applied, so everything that reads a
/// mailbox out of a served record — synchronization, delivery, the rules, classification, scanning, and contact
/// collection — reads its effective settings, and every rule the binder applies is applied to the effective account.
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

            // Keyed by the policies themselves rather than by their text: the accounts read for one user arrive with
            // the one instance read beside them, so the policy is judged once for the set rather than once per account.
            var read = new Dictionary<GoverningSettingsPolicies, SettingsPolicyLayers?>(ReferenceEqualityComparer.Instance);

            foreach (var (position, account) in served.Index())
            {
                if (!read.TryGetValue(account.Policies, out var layers))
                {
                    layers = MailAccountPolicyResolution.LayersOf(account, out _);
                    read[account.Policies] = layers;
                }

                keyed[position.ToString(CultureInfo.InvariantCulture)] = DeclarationOf(account, layers);
            }

            record[MailAccountsProperty] = keyed;
        }

        return record.ToJsonString();
    }

    /// <summary>Composes a record that states nothing but one account's declaration.</summary>
    /// <param name="declaration">The declaration, as <see cref="DeclarationOf(MailAccountRecord, SettingsPolicyLayers?)" /> composes it.</param>
    /// <returns>The record a binder judges that one account in.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaration" /> is <see langword="null" />.</exception>
    /// <remarks>What a policy write judges an account by: the account on its own, with no user around it whose record could refuse it.</remarks>
    internal static string ComposeAlone(JsonNode declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return new JsonObject { [MailAccountsProperty] = new JsonObject { ["0"] = declaration } }.ToJsonString();
    }

    /// <summary>Reports whether an account is one a user can be served, which is an account holding an address.</summary>
    /// <param name="account">The account.</param>
    /// <returns><see langword="true" /> when the account states an address.</returns>
    internal static bool IsServed(MailAccountRecord account) =>
        !string.IsNullOrWhiteSpace(account?.EmailAddress);

    /// <summary>Composes the declaration an account is bound as, its identifier and name standing beside its effective settings.</summary>
    /// <param name="account">The account, carrying the policies read beside it.</param>
    /// <returns>The declaration, or a JSON string standing in for one no binder takes, so the binder refuses it by name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal static JsonNode DeclarationOf(MailAccountRecord account) =>
        DeclarationOf(account, MailAccountPolicyResolution.LayersOf(account, out _));

    /// <summary>Composes the declaration an account is bound as under one reading of the policies governing it.</summary>
    /// <param name="account">The account.</param>
    /// <param name="layers">What the policies governing the account say about a mail account, or <see langword="null" /> where this build does not read one of them.</param>
    /// <returns>The declaration, or a JSON string standing in for one no binder takes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Two accounts are composed as a declaration no binder takes: one whose document is not a JSON object, and one a
    /// policy this build does not read governs. The second is held back for the reason a user's record is — an
    /// account is never served on part of what governs it.
    /// </remarks>
    internal static JsonNode DeclarationOf(MailAccountRecord account, SettingsPolicyLayers? layers)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (layers is null
            || GovernedRecordResolution.ReadDocument(MailAccountPolicyResolution.Resolve(account, layers).EffectiveJson)
                is not { } declaration)
        {
            return JsonValue.Create(account.Document);
        }

        declaration[AccountIdProperty] = account.Id.ToString("D");

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
