// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers with one account's collection settings, whichever account is asked about.</summary>
/// <remarks>
/// A test arranges what one account collects and synchronizes that account, so distinguishing accounts here would only
/// let a test spell an identifier wrong and see collection do nothing.
/// </remarks>
internal sealed class StubContactCollectionSettingsReader(ContactCollectionSettings settings)
    : IContactCollectionSettingsReader
{
    /// <summary>Answers with an account that collects nothing, which is what every deployment starts as.</summary>
    internal static StubContactCollectionSettingsReader CollectingNothing { get; } =
        new(ContactCollectionSettings.CollectingNothing);

    /// <inheritdoc />
    public ContactCollectionSettings SettingsFor(MailAccountId accountId) => settings;
}
