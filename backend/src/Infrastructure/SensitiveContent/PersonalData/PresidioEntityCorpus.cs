// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.SensitiveContent;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>Which analyzer entities each personal-data category is made of, and the revision of that mapping.</summary>
/// <remarks>
/// <para>
/// <b>The mapping is explicit rather than pass-through.</b> An operator configures MailFathom's categories, never the
/// analyzer's vocabulary: entity names are a third party's identifiers, they change between analyzer releases, and a
/// deployment that named them would be configured against a service rather than against this product. So a category is
/// declared here with the entities it covers, one entity is one rule inside it, and an operator meeting a single
/// misfiring recognizer suppresses that entity by name inside a category that stays on.
/// </para>
/// <para>
/// A rule is spelled exactly as the analyzer spells the entity, which is what makes the request this scanner sends and
/// the answer it maps back the same list read twice. It is also why the mapping is total in one direction only: an entity
/// this table does not name is ignored when it arrives, because an analyzer running recognizers of its own is entitled to
/// report things this deployment never asked about.
/// </para>
/// <para>
/// What the table deliberately leaves out is as much a decision as what it holds. Company registration, business, and
/// organisation numbers name a legal entity rather than a person. Vehicle registrations and licence plates are omitted
/// because the categories this scanner ships are the ones a mailbox owner is harmed by, and a plate matches ordinary
/// prose often enough to empty a chunk store on its own. The analyzer's clinical entities — a disease, a medication, a
/// procedure — are health narrative rather than health identifiers, and hiding them turns a message about a patient into
/// a message about nothing; <see cref="PersonalDataCategories.HealthIdentifier" /> covers what names a person inside a
/// health system. Nationality, religious, and political affiliation is left out for a different reason: it is exactly the
/// special category that deserves the strongest treatment, and the analyzer's own answer for it is a named-entity label
/// with a false-positive rate that would make the category unusable rather than protective.
/// </para>
/// <para>
/// Two more of the shipped analyzer's own entities are left out for reasons of their own. A cryptocurrency wallet address
/// names an account rather than a person, and unlike a card or a bank account number it is published by design in the
/// systems that use it, so hiding it protects nobody. A URL is the structure of a message rather than an identifier inside
/// one: a category that redacted every link would empty most mail, and the identifiers a link can carry in its query are
/// the concern of the secret scanner beside this one.
/// </para>
/// </remarks>
internal static class PresidioEntityCorpus
{
    /// <summary>The revision of this mapping, which every finding carries as part of its detector identity.</summary>
    /// <remarks>
    /// It moves whenever a category gains, loses, or re-homes an entity, because redaction is only reproducible against a
    /// stated profile: the same text under a changed mapping is a different result, and something that stored one has to
    /// be able to say which one it stored. It is deliberately not the analyzer's own version — the analyzer publishes
    /// none over its API, and this is the part MailFathom is responsible for.
    /// </remarks>
    public const string MappingRevision = "1";

    /// <summary>The name every finding this scanner produces is attributed to.</summary>
    public const string DetectorName = "mailfathom-personal-data";

    private static readonly IReadOnlyList<CategoryEntities> Mapping =
    [
        new(PersonalDataCategories.PaymentCard, ["CREDIT_CARD"]),
        new(PersonalDataCategories.BankAccount, ["IBAN_CODE", "US_BANK_NUMBER"]),
        new(
            PersonalDataCategories.NationalIdentifier,
            [
                "AU_TFN",
                "CA_SIN",
                "DE_SOCIAL_SECURITY",
                "DE_TAX_ID",
                "DE_TAX_NUMBER",
                "ES_NIE",
                "ES_NIF",
                "FI_PERSONAL_IDENTITY_CODE",
                "IN_AADHAAR",
                "IN_GSTIN",
                "IN_PAN",
                "IN_VOTER",
                "IT_FISCAL_CODE",
                "IT_VAT_CODE",
                "KR_FRN",
                "KR_RRN",
                "NG_NIN",
                "PH_TIN",
                "PL_PESEL",
                "SE_PERSONNUMMER",
                "SG_NRIC_FIN",
                "TH_TNIN",
                "TR_NATIONAL_ID",
                "UK_NINO",
                "US_ITIN",
                "US_SSN",
                "ZA_ID_NUMBER",
            ]),
        new(
            PersonalDataCategories.IdentityDocument,
            [
                "DE_ID_CARD",
                "DE_PASSPORT",
                "ES_PASSPORT",
                "IN_PASSPORT",
                "IT_DRIVER_LICENSE",
                "IT_IDENTITY_CARD",
                "IT_PASSPORT",
                "KR_DRIVER_LICENSE",
                "KR_PASSPORT",
                "UK_DRIVING_LICENCE",
                "UK_PASSPORT",
                "US_DRIVER_LICENSE",
                "US_PASSPORT",
            ]),
        new(
            PersonalDataCategories.HealthIdentifier,
            ["AU_MEDICARE", "DE_HEALTH_INSURANCE", "MEDICAL_LICENSE", "UK_NHS", "US_MBI", "US_NPI"]),
        new(PersonalDataCategories.PersonName, ["PERSON"]),
        new(PersonalDataCategories.EmailAddress, ["EMAIL_ADDRESS"]),
        new(PersonalDataCategories.PostalAddress, ["DE_PLZ", "LOCATION", "UK_POSTCODE"]),
        new(PersonalDataCategories.PhoneNumber, ["PHONE_NUMBER"]),
        new(PersonalDataCategories.Date, ["DATE_TIME"]),
        new(PersonalDataCategories.NetworkAddress, ["IP_ADDRESS", "MAC_ADDRESS"]),
    ];

    /// <summary>Every rule the scanner can look for, in the order the catalog declares its categories.</summary>
    public static IReadOnlyList<SensitiveContentRule> Rules { get; } =
    [
        .. Mapping.SelectMany(entry => entry.Entities.Select(entity =>
            SensitiveContentRule.Create(entry.Category, entity))),
    ];

    /// <summary>The rules inside one category.</summary>
    /// <param name="category">The category to read.</param>
    /// <returns>Every rule the category holds, which is one per analyzer entity it covers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="category" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<SensitiveContentRule> RulesOf(SensitiveContentCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return [.. Rules.Where(rule => rule.Category == category)];
    }

    /// <summary>The rules a deployment's plan actually asks the analyzer about.</summary>
    /// <param name="plan">What this deployment scans for, of which the personal-data half is read.</param>
    /// <returns>The rules inside the planned categories that no suppression silences, indexed by the entity they name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the plan does not switch the personal-data scanner on.</exception>
    /// <remarks>
    /// One answer serves the scanner and the startup probe, so what is requested of the analyzer and what a startup
    /// failure is judged against cannot disagree. A suppressed rule is left out of the request rather than filtered out of
    /// the answer, so an entity an operator switched off costs the analyzer nothing to look for.
    /// </remarks>
    public static FrozenDictionary<string, SensitiveContentRule> RequestedRules(SensitiveContentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.TryGetScanner(SensitiveContentScannerKind.Pii, out var personalData))
        {
            throw new ArgumentException(
                "The personal-data scanner was constructed for a deployment whose plan does not switch it on.",
                nameof(plan));
        }

        return Rules
            .Where(rule => personalData.Categories.Contains(rule.Category))
            .Where(rule => !personalData.Suppresses(rule))
            .ToFrozenDictionary(rule => rule.Name, StringComparer.Ordinal);
    }

    private sealed record CategoryEntities(SensitiveContentCategory Category, IReadOnlyList<string> Entities);
}
