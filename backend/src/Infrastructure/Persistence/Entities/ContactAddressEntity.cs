// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One address a person in the contact book uses.</summary>
/// <remarks>
/// The comparison form is unique across the whole table rather than within one contact, which is the rule that keeps a
/// book from holding one person twice: whoever claims an address second is refused by the database rather than by a
/// check that two callers could both pass.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactAddressEntity
{
    /// <summary>The longest address stored, which is the bound the domain already refuses to exceed.</summary>
    internal const int MaximumAddressLength = Contact.MaximumAddressLength;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the contact this address belongs to.</summary>
    public Guid ContactId { get; set; }

    /// <summary>Gets or sets the address as it was written, which is what a reader is shown.</summary>
    public required string Address { get; set; }

    /// <summary>Gets or sets the comparison form, which is what anything matches, groups, or indexes on.</summary>
    public required string NormalizedAddress { get; set; }
}
