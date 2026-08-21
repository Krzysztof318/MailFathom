// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>The record a caller wants one contact to have, and the origin it is asking under.</summary>
/// <remarks>
/// <para>
/// An amendment states the whole record rather than the difference from the one held. Adding an address, dropping one,
/// choosing a different default, and correcting a name are then one operation whose result the invariants can be checked
/// against, instead of four that could each leave a contact without an address or with a default it does not hold.
/// </para>
/// <para>
/// <see cref="Writer" /> is the authority rather than a value to store: a contact is amendable only by a writer of its
/// own origin, and nothing about an amendment can change which origin a contact has. Promotion is the one act that does.
/// </para>
/// </remarks>
public sealed record ContactAmendment
{
    /// <summary>Gets the contact to amend.</summary>
    public required ContactId ContactId { get; init; }

    /// <summary>Gets the origin the writer acts under.</summary>
    public required ContactOrigin Writer { get; init; }

    /// <summary>Gets the name the contact is to carry.</summary>
    public required ContactDisplayName DisplayName { get; init; }

    /// <summary>Gets every address the contact is to hold afterwards.</summary>
    public required IReadOnlyCollection<EmailAddress> Addresses { get; init; }

    /// <summary>Gets the address to use by default, which must be one of <see cref="Addresses" />.</summary>
    public required EmailAddress PreferredAddress { get; init; }

    /// <summary>Gets what the owner wrote about this person, or <see langword="null" /> to hold no note.</summary>
    public ContactNote? Note { get; init; }
}
