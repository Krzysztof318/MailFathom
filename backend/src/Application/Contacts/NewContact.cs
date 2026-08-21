// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>The person a caller is asking the book to hold, before the book has given them an identity.</summary>
/// <remarks>
/// The identity is deliberately absent. It is MailFathom's own and is minted where the record is written, so no caller
/// can choose one, reuse one, or derive one from an address — which is the property the whole book rests on.
/// </remarks>
public sealed record NewContact
{
    /// <summary>Gets the name to record for this person.</summary>
    public required ContactDisplayName DisplayName { get; init; }

    /// <summary>Gets every address this person uses; two spellings of one address count once.</summary>
    public required IReadOnlyCollection<EmailAddress> Addresses { get; init; }

    /// <summary>Gets the address to use by default, which must be one of <see cref="Addresses" />.</summary>
    public required EmailAddress PreferredAddress { get; init; }

    /// <summary>Gets what the owner wrote about this person, or <see langword="null" /> for none.</summary>
    public ContactNote? Note { get; init; }

    /// <summary>Gets the origin the writer acts under, which becomes the record's own.</summary>
    public required ContactOrigin Origin { get; init; }
}
