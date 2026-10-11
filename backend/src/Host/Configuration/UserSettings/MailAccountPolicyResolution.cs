// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Resolves a mail account through the settings policies that govern it.</summary>
/// <remarks>
/// <para>
/// What a policy governs of an account is its declaration: the settings its document states and the display name
/// its row holds beside them, which the account's own type declares as a property like any other. The address is a
/// column no document states, so a policy says only who may change it, and the value answered for it is the row's.
/// </para>
/// <para>
/// The account's own document is never changed. What comes back is the declaration every reader of the account binds
/// — synchronization, delivery, the rules, classification, scanning, and contact collection all read an account out
/// of the record its user is served from, and that record is composed from this.
/// </para>
/// </remarks>
internal static class MailAccountPolicyResolution
{
    /// <summary>Reads what the policies an account was read with say about a mail account.</summary>
    /// <param name="account">The account, carrying the policies read beside it.</param>
    /// <param name="unreadable">Which scope's policy this build does not judge as one, where the answer is <see langword="null" />.</param>
    /// <returns>The layers, or <see langword="null" /> when this build does not judge one of the policies as a settings policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal static SettingsPolicyLayers? LayersOf(MailAccountRecord account, out SettingsPolicyScope unreadable)
    {
        ArgumentNullException.ThrowIfNull(account);

        return SettingsPolicyLayers.Read(account.Policies, SettingsPolicySection.MailAccounts, out unreadable);
    }

    /// <summary>Composes the declaration a policy governs: the account's settings with the display name its row holds.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The declaration, or the account's document unchanged where it is not a JSON object, which every reader of the answer refuses in its own words.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal static string GovernedDeclarationOf(MailAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (GovernedRecordResolution.ReadDocument(account.Document) is not { } declaration)
        {
            return account.Document;
        }

        MailAccountRecordComposition.RemoveEverySpelling(declaration, MailAccountRecordComposition.AccountIdProperty);
        MailAccountRecordComposition.RemoveEverySpelling(declaration, MailAccountRecordComposition.DisplayNameProperty);
        MailAccountRecordComposition.RemoveEverySpelling(declaration, MailAccountRecordComposition.EmailAddressProperty);

        declaration[MailAccountRecordComposition.DisplayNameProperty] = account.DisplayName;

        return declaration.ToJsonString();
    }

    /// <summary>Resolves one account under one reading of the policies governing it.</summary>
    /// <param name="account">The account.</param>
    /// <param name="layers">What the policies governing the account say about a mail account.</param>
    /// <returns>The declaration the account is served from, and where each of its properties came from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ResolvedRecord Resolve(MailAccountRecord account, SettingsPolicyLayers layers)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(layers);

        var resolved = GovernedRecordResolution.Resolve(
            SettingsPolicySection.MailAccounts,
            layers,
            GovernedDeclarationOf(account));

        return resolved with
        {
            Properties =
            [
                // The address is a column beside the document, so the resolution holds no value for it and the row
                // read with the account does.
                .. resolved.Properties.Select(property =>
                    property.Property.Path == MailAccountRecordComposition.EmailAddressProperty && account.EmailAddress is { } address
                        ? property with { EffectiveValue = JsonValue.Create(address) }
                        : property),
            ],
        };
    }

    /// <summary>Resolves one account under the policies it was read with.</summary>
    /// <param name="account">The account, carrying the policies read beside it.</param>
    /// <returns>The account as it resolves, or <see langword="null" /> where a policy governing it is not one this build reads.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal static ResolvedRecord? ResolveUnderItsPolicies(MailAccountRecord account) =>
        LayersOf(account, out _) is { } layers ? Resolve(account, layers) : null;
}
