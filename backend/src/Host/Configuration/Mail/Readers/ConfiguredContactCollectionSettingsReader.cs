// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    /// an account nobody configures has no owner to have asked for a book.
    /// </remarks>
    public ContactCollectionSettings GetContactCollectionSettings(MailAccountId accountId) =>
        this.settingsByAccount.Value.TryGetValue(accountId.Value, out var accountSettings)
            ? accountSettings
            : ContactCollectionSettings.CollectingNothing;

    /// <summary>Builds every account's collection settings once, keyed by the account identifier the lookups arrive with.</summary>
    /// <remarks>
    /// The own addresses are read once for the whole deployment and handed to every account's policy, because an owner
    /// writing from one of their mailboxes to another is not a correspondent of themselves. An entry whose text is
    /// unusable is skipped and two accounts configured under one identifier keep the first, both for the reason the
    /// trust policies do: startup validation refuses each of those, and a reload being rejected must not make an
    /// arriving message throw.
    /// </remarks>
    private Dictionary<string, ContactCollectionSettings> ReadSettings()
    {
        var ownAddresses = this.ReadOwnAccountAddresses();

        return (this.settings.Accounts ?? [])
            .Select(static account => (
                Id: MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                account.ContactCollection))
            .Where(static account => account.Id is not null && account.ContactCollection is not null)
            .GroupBy(static account => account.Id!, StringComparer.Ordinal)
            .ToDictionary(
                static account => account.Key,
                account => ReadContactCollection(account.First().ContactCollection!, ownAddresses),
                StringComparer.Ordinal);
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

    /// <summary>Reads the mailboxes this deployment reads on its owner's behalf.</summary>
    /// <remarks>
    /// Derived from each account's user name for the reason the trusted own domains are: it is the only mailbox
    /// identity an IMAP account states. An account whose user name is a bare login contributes nothing, which costs one
    /// address that would have been excluded — and the two headers collection reads leave the owner out of both
    /// directions anyway, since an ordinary folder's author is a correspondent and a sent folder's recipients are.
    /// </remarks>
    private IReadOnlyList<EmailAddress> ReadOwnAccountAddresses() =>
    [
        .. (this.settings.Accounts ?? [])
            .Select(static account => EmailAddress.TryCreate(displayName: null, account.UserName, out var address)
                ? address
                : (EmailAddress?)null)
            .OfType<EmailAddress>()
            .Distinct(),
    ];
}
