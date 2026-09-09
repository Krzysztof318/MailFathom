// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts.Collection;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads what an account collects contacts from, and which correspondents it leaves out, from the bound section.</summary>
/// <remarks>
/// The settings are built once for the snapshot this reader was constructed over rather than per lookup, because every
/// message of a switched-on account asks for them and building one reads every account's own mailbox address. The
/// build is deferred rather than done in the constructor, so a deployment that collects nothing never walks the
/// accounts.
/// </remarks>
internal sealed class ConfiguredContactCollectionSettingsReader : IContactCollectionSettingsReader
{
    private readonly MailSynchronizationOptions settings;

    private readonly Lazy<IReadOnlyDictionary<string, ContactCollectionSettings>> settingsByAccount;

    /// <summary>Initializes the reader over one snapshot of the mail section.</summary>
    /// <param name="settings">The snapshot the collection settings are read from.</param>
    internal ConfiguredContactCollectionSettingsReader(MailSynchronizationOptions settings)
    {
        this.settings = settings;
        this.settingsByAccount = new(this.ReadSettings, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names collects nothing, which is the honest answer as well as the safe one:
    /// an account nobody configures has no user to have asked for a book.
    /// </remarks>
    public ContactCollectionSettings GetContactCollectionSettings(MailAccountId accountId) =>
        this.settingsByAccount.Value.TryGetValue(accountId.Value, out var accountSettings)
            ? accountSettings
            : ContactCollectionSettings.CollectingNothing;

    /// <summary>Builds every account's collection settings once, keyed by the account identifier the lookups arrive with.</summary>
    /// <remarks>
    /// The own addresses are read once per owner and handed to each of that person's accounts, because a user writing
    /// from one of their mailboxes to another is not a correspondent of themselves. An entry whose text is
    /// unusable is skipped and two accounts configured under one identifier keep the first, both for the reason the
    /// trust policies do: startup validation refuses each of those, and a reload being rejected must not make an
    /// arriving message throw. The accounts come from
    /// <see cref="MailSynchronizationOptions.DeclaredAccountsByOwner" />, so a deployment that declares its mailboxes
    /// under its served users collects what it configured rather than nothing, and the own addresses one account is
    /// read against are that person's own rather than every served user's.
    /// </remarks>
    private Dictionary<string, ContactCollectionSettings> ReadSettings() =>
        this.settings.DeclaredAccountsByOwner
            .SelectMany(static owned => SettingsOf(owned))
            .Where(static account => account.Id is not null)
            .GroupBy(static account => account.Id!, StringComparer.Ordinal)
            .ToDictionary(
                static account => account.Key,
                static account => account.First().Settings,
                StringComparer.Ordinal);

    /// <summary>Builds the collection settings of the mailboxes one person owns, over the addresses those same mailboxes state.</summary>
    private static IEnumerable<(string? Id, ContactCollectionSettings Settings)> SettingsOf(
        IReadOnlyList<MailSynchronizationAccountOptions> owned)
    {
        var ownAddresses = ReadOwnAccountAddresses(owned);

        return owned
            .Where(static account => account.ContactCollection is not null)
            .Select(account => (
                MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                ReadContactCollection(account.ContactCollection!, ownAddresses)));
    }

    /// <summary>Reads one account's configured block as the settings collection runs under.</summary>
    private static ContactCollectionSettings ReadContactCollection(
        ContactCollectionOptions configured,
        IReadOnlyCollection<EmailAddress> ownAddresses) => new()
        {
            IsEnabled = configured.Enabled,
            MinimumMessagesFromSender = configured.MinimumMessagesFromSender,
            MaxContactsPerRun = configured.MaxContactsPerRun,
            Policy = ContactCollectionPolicy.Create(configured.ConfiguredExclusions, ownAddresses),
        };

    /// <summary>Reads the mailboxes this deployment reads on one person's behalf.</summary>
    /// <remarks>
    /// Derived from each account's user name for the reason the trusted own domains are: it is the only mailbox
    /// identity an IMAP account states. An account whose user name is a bare login contributes nothing, which costs one
    /// address that would have been excluded — and the two headers collection reads leave the user out of both
    /// directions anyway, since an ordinary folder's author is a correspondent and a sent folder's recipients are.
    /// </remarks>
    private static IReadOnlyList<EmailAddress> ReadOwnAccountAddresses(
        IReadOnlyList<MailSynchronizationAccountOptions> owned) =>
    [
        .. owned
            .Select(static account => EmailAddress.TryCreate(displayName: null, account.UserName, out var address)
                ? address
                : (EmailAddress?)null)
            .OfType<EmailAddress>()
            .Distinct(),
    ];
}
