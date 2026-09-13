// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One mail account as somebody declares it: its address and display name beside the settings it is read with.</summary>
/// <param name="EmailAddress">The mailbox's address, trimmed.</param>
/// <param name="DisplayName">The name the account is told apart by among one user's accounts.</param>
/// <param name="Document">The settings, as a JSON object carrying neither of the two above.</param>
/// <remarks>
/// The address and the name are columns of the account's record rather than settings, and the declaration is the one
/// document that carries all three, so what an administrator reads, edits, and saves back is one object. The identifier
/// is not part of it: this deployment generates it, and a declaration that stated one would decide an identity it does
/// not own.
/// </remarks>
internal sealed record MailAccountDeclaration(string EmailAddress, string DisplayName, string Document)
{
    /// <summary>Reads a declaration.</summary>
    /// <param name="declarationJson">The declaration as it was written.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarationJson" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the declaration is not a JSON object, states an identifier, or states no usable address or display name.</exception>
    /// <exception cref="JsonException">Thrown when the declaration is not JSON at all.</exception>
    public static MailAccountDeclaration Read(string declarationJson)
    {
        ArgumentNullException.ThrowIfNull(declarationJson);

        var declaration = JsonNode.Parse(declarationJson) as JsonObject
            ?? throw new FormatException("A mail-account declaration is a JSON object of that account's settings, and this is not one.");

        if (SpellingsOf(declaration, MailAccountRecordComposition.AccountIdProperty).Length > 0)
        {
            throw new FormatException(
                $"A mail-account declaration states no {MailAccountRecordComposition.AccountIdProperty}: this deployment generates the identifier, and an account is changed by naming the one it was given.");
        }

        var emailAddress = TextOf(declaration, MailAccountRecordComposition.EmailAddressProperty)?.Trim();
        var displayName = TextOf(declaration, MailAccountRecordComposition.DisplayNameProperty);

        if (!IsAnAddress(emailAddress))
        {
            throw new FormatException(
                $"A mail-account declaration states the mailbox's {MailAccountRecordComposition.EmailAddressProperty}: one '@' between a local part and a domain, no white space, and at most {MailAccountRecord.MaximumEmailAddressLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new FormatException(
                $"A mail-account declaration states the {MailAccountRecordComposition.DisplayNameProperty} the account is told apart by.");
        }

        MailAccountRecordComposition.RemoveEverySpelling(declaration, MailAccountRecordComposition.EmailAddressProperty);
        MailAccountRecordComposition.RemoveEverySpelling(declaration, MailAccountRecordComposition.DisplayNameProperty);

        return new MailAccountDeclaration(emailAddress!, displayName, declaration.ToJsonString());
    }

    /// <summary>Writes the declaration an account's record holds, unredacted.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The declaration JSON, its address and name first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the stored settings are not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the stored settings are not JSON at all.</exception>
    public static string Of(MailAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var settings = JsonNode.Parse(account.Document) as JsonObject
            ?? throw new FormatException("The mail account's stored settings are not a JSON object.");

        var declaration = new JsonObject();

        if (account.EmailAddress is { } emailAddress)
        {
            declaration[MailAccountRecordComposition.EmailAddressProperty] = emailAddress;
        }

        declaration[MailAccountRecordComposition.DisplayNameProperty] = account.DisplayName;

        foreach (var (name, value) in settings.ToArray())
        {
            if (!IsAColumn(name))
            {
                settings.Remove(name);
                declaration[name] = value;
            }
        }

        return declaration.ToJsonString();
    }

    /// <summary>Reports whether a declaration property is one the record keeps as a column rather than a setting.</summary>
    private static bool IsAColumn(string name) =>
        name.Equals(MailAccountRecordComposition.AccountIdProperty, StringComparison.OrdinalIgnoreCase)
        || name.Equals(MailAccountRecordComposition.EmailAddressProperty, StringComparison.OrdinalIgnoreCase)
        || name.Equals(MailAccountRecordComposition.DisplayNameProperty, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports whether a value is shaped like a mailbox address.</summary>
    /// <remarks>
    /// Shape rather than deliverability: the address is what one account in the deployment is told apart by, and the
    /// server it names is what proves it. One '@' is the whole rule because a quoted local part carrying a second one
    /// is an address no mail provider hands out.
    /// </remarks>
    private static bool IsAnAddress(string? value) =>
        value is { Length: > 0 and <= MailAccountRecord.MaximumEmailAddressLength }
        && !value.Any(char.IsWhiteSpace)
        && value.Count(character => character == '@') == 1
        && value[0] != '@'
        && value[^1] != '@';

    private static string? TextOf(JsonObject declaration, string property) =>
        SpellingsOf(declaration, property) is [var spelling]
        && declaration[spelling] is JsonValue value
        && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static string[] SpellingsOf(JsonObject declaration, string property) =>
    [
        .. declaration.Select(entry => entry.Key).Where(key => key.Equals(property, StringComparison.OrdinalIgnoreCase)),
    ];
}
