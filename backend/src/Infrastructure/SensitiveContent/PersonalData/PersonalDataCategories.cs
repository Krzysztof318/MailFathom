// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>The kinds of personal data this scanner looks for, which are the names an operator configures it by.</summary>
/// <remarks>
/// <para>
/// The five default categories are the identifiers that are high harm and low ambiguity: a payment instrument, a bank
/// account, a government or tax number, a travel or driving document, a health identifier. Each of them is worth hiding
/// wherever it appears, and nothing about the surrounding message makes one of them safe.
/// </para>
/// <para>
/// The six that are off by default are what a mailbox is made of. Every message carries a name, an address, a signature
/// block, and a date, so switching one of these on empties the chunk store of the terms a search runs on — a legitimate
/// choice under a strict regime and a poor default, so it is the operator's decision rather than the product's.
/// </para>
/// <para>
/// These are MailFathom's own names. Which analyzer entities each one maps onto is
/// <see cref="PresidioEntityCorpus" />'s answer, because an operator configures categories and never the analyzer's
/// vocabulary.
/// </para>
/// </remarks>
internal static class PersonalDataCategories
{
    /// <summary>A payment card number.</summary>
    public static SensitiveContentCategory PaymentCard { get; } = SensitiveContentCategory.Create("PaymentCard");

    /// <summary>An IBAN or another identifier of a bank account.</summary>
    public static SensitiveContentCategory BankAccount { get; } = SensitiveContentCategory.Create("BankAccount");

    /// <summary>A national identification or tax number a government issued to a person.</summary>
    public static SensitiveContentCategory NationalIdentifier { get; } =
        SensitiveContentCategory.Create("NationalIdentifier");

    /// <summary>A passport or driving-licence number.</summary>
    public static SensitiveContentCategory IdentityDocument { get; } =
        SensitiveContentCategory.Create("IdentityDocument");

    /// <summary>An identifier that names a person inside a health system, or the licence of someone practising in one.</summary>
    public static SensitiveContentCategory HealthIdentifier { get; } =
        SensitiveContentCategory.Create("HealthIdentifier");

    /// <summary>A personal name.</summary>
    public static SensitiveContentCategory PersonName { get; } = SensitiveContentCategory.Create("PersonName");

    /// <summary>An email address.</summary>
    public static SensitiveContentCategory EmailAddress { get; } = SensitiveContentCategory.Create("EmailAddress");

    /// <summary>A postal address, or a place named precisely enough to be one.</summary>
    public static SensitiveContentCategory PostalAddress { get; } = SensitiveContentCategory.Create("PostalAddress");

    /// <summary>A telephone number.</summary>
    public static SensitiveContentCategory PhoneNumber { get; } = SensitiveContentCategory.Create("PhoneNumber");

    /// <summary>A date or a time, absolute or relative.</summary>
    public static SensitiveContentCategory Date { get; } = SensitiveContentCategory.Create("Date");

    /// <summary>An address that identifies a machine, whether the network assigned it or the hardware carries it.</summary>
    public static SensitiveContentCategory NetworkAddress { get; } = SensitiveContentCategory.Create("NetworkAddress");

    /// <summary>Every category, in the order a catalog declares them and a finding is reported under.</summary>
    public static IReadOnlyList<SensitiveContentCategory> All { get; } =
    [
        PaymentCard,
        BankAccount,
        NationalIdentifier,
        IdentityDocument,
        HealthIdentifier,
        PersonName,
        EmailAddress,
        PostalAddress,
        PhoneNumber,
        Date,
        NetworkAddress,
    ];

    /// <summary>The categories a deployment that names none of its own is scanned for.</summary>
    public static IReadOnlyList<SensitiveContentCategory> DetectedByDefault { get; } =
    [
        PaymentCard,
        BankAccount,
        NationalIdentifier,
        IdentityDocument,
        HealthIdentifier,
    ];

    /// <summary>Reports whether a category is looked for by a deployment that names none of its own.</summary>
    /// <param name="category">The category to judge.</param>
    /// <returns><see langword="true" /> for the strong identifiers and <see langword="false" /> for what a mailbox is made of.</returns>
    public static bool IsDetectedByDefault(SensitiveContentCategory category) =>
        DetectedByDefault.Contains(category);
}
