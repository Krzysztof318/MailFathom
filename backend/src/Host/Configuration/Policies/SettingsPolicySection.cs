// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>The half of a settings policy about one kind of record.</summary>
/// <remarks>
/// The two sections are the whole of what differs between governing a user's record and governing a mail account:
/// which type the record binds as, which of its identity properties are columns beside the document, and which of its
/// values are carried as written names the binder cannot refuse. Everything a policy is judged by is asked of a
/// section through these, so nothing in the judgement names a property of either record.
/// </remarks>
internal sealed class SettingsPolicySection
{
    /// <summary>The section about a user's record, with the display name that is a column beside it.</summary>
    internal static readonly SettingsPolicySection Users = new(
        "Users",
        "a user's record",
        typeof(UserAccountOptions),
        GovernableProperties.Of(typeof(UserAccountOptions), "DisplayName"),
        record => ((UserAccountOptions)record).FindRefusals().Select(refusal => refusal.ErrorMessage).OfType<string>());

    /// <summary>The section about a mail account, with the address that is a column beside its declaration.</summary>
    internal static readonly SettingsPolicySection MailAccounts = new(
        "MailAccounts",
        "a mail account",
        typeof(MailSynchronizationAccountOptions),
        GovernableProperties.Of(
            typeof(MailSynchronizationAccountOptions),
            MailAccountRecordComposition.EmailAddressProperty),
        record => FindUnwritableLanguage((MailSynchronizationAccountOptions)record));

    private SettingsPolicySection(
        string name,
        string governs,
        Type recordType,
        GovernableProperties properties,
        Func<object, IEnumerable<string>> findUnknownWrittenNames)
    {
        this.Name = name;
        this.Governs = governs;
        this.RecordType = recordType;
        this.Properties = properties;
        this.FindUnknownWrittenNames = findUnknownWrittenNames;
    }

    /// <summary>Gets both sections, in the order a policy is read.</summary>
    internal static IReadOnlyList<SettingsPolicySection> All { get; } = [Users, MailAccounts];

    /// <summary>Gets the key the section is written under.</summary>
    internal string Name { get; }

    /// <summary>Gets what the section governs, as a refusal names it.</summary>
    internal string Governs { get; }

    /// <summary>Gets the type a governed record's document binds as, which a sparse statement of this section binds as too.</summary>
    internal Type RecordType { get; }

    /// <summary>Gets the properties of the governed record a policy may name.</summary>
    internal GovernableProperties Properties { get; }

    /// <summary>Gets what refuses a value the record carries as a written name, which the binder accepts as any text.</summary>
    /// <remarks>
    /// A language, a zone, and a recording level are stated by name and bound as text, so that an unknown one is
    /// refused in a sentence naming what the setting takes rather than in the binder's own. That leaves the binder
    /// unable to refuse one, so the record's own rule is asked of a statement bound sparsely — every such rule being
    /// about one stated value and nothing beside it.
    /// </remarks>
    internal Func<object, IEnumerable<string>> FindUnknownWrittenNames { get; }

    private static IEnumerable<string> FindUnwritableLanguage(MailSynchronizationAccountOptions account)
    {
        if (!string.IsNullOrWhiteSpace(account.Language) && account.ReadingLanguage is null)
        {
            yield return
                $"{nameof(account.Language)} states '{account.Language}', which is not a language MailFathom writes in. It takes {string.Join(" or ", Enum.GetNames<MailAccountLanguage>().Select(language => $"'{language}'"))}.";
        }
    }
}
