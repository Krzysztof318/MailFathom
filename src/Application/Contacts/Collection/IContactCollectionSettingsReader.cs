// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Contacts.Collection;

/// <summary>Answers what one account collects from its arriving mail.</summary>
/// <remarks>
/// Read per message rather than captured once, so an operator switching collection off stops it at the next message
/// instead of at the next restart. An account the configuration no longer names collects nothing, which is the honest
/// answer as well as the safe one: an account nobody configured has no owner to have asked for a book.
/// </remarks>
public interface IContactCollectionSettingsReader
{
    /// <summary>Reads what one account collects.</summary>
    /// <param name="accountId">The account whose mail is being synchronized.</param>
    /// <returns>The settings, or <see cref="ContactCollectionSettings.CollectingNothing" /> for an account this deployment does not configure.</returns>
    ContactCollectionSettings GetContactCollectionSettings(MailAccountId accountId);
}
