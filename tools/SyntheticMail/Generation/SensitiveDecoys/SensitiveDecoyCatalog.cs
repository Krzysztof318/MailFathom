// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>Everything a generated message may be caught carrying, and what is expected to catch it.</summary>
/// <remarks>
/// <para>
/// The catalog covers every category the two scanners look for <i>by default</i>, and stops there. A deployment that
/// names no categories of its own scans for six kinds of secret and five kinds of personal identifier, so a corpus
/// carrying one of each exercises the whole default configuration; the entropy heuristic is deliberately absent,
/// because it is off unless an operator names it and a corpus that tripped it everywhere would say nothing about the
/// categories beside it.
/// </para>
/// <para>
/// <b>Two entries share a category on purpose.</b> A national identification number is found by a recogniser
/// registered for one language, so the same category is reached through a Polish number and an American one and a
/// deployment finds whichever its analyzer was asked in. Everything else is either language-agnostic — a payment card,
/// an account number, a medical licence — or reached through the English-language recogniser that a default
/// deployment loads.
/// </para>
/// <para>
/// Nothing in a message says a decoy is one. A corpus whose planted lines were marked would test a scanner against
/// text that announces itself, and the mail this tool exists to imitate does the opposite: a credential arrives in a
/// mailbox inside a sentence somebody wrote in a hurry. What names them instead is the corpus listing, which reports
/// the category and never the value, exactly as a finding does.
/// </para>
/// <para>
/// <b>Every sentence here is ASCII.</b> A decoy is planted without regard to the charset the message it lands in is
/// encoded with, and this generator writes bodies in <c>us-ascii</c> and <c>iso-8859-1</c> as readily as in
/// <c>utf-8</c>, so a sentence reaching past ASCII would be delivered with question marks in place of whatever the
/// encoder could not represent. The vocabulary's closing lines can carry accents because each is drawn only for the
/// charset that holds it; a decoy is drawn for every charset there is.
/// </para>
/// </remarks>
internal static class SensitiveDecoyCatalog
{
    /// <summary>The name of the scanner that looks for a credential, as a deployment configures it.</summary>
    private const string SecretScanner = "Secrets";

    /// <summary>The name of the scanner that looks for personal data, as a deployment configures it.</summary>
    private const string PersonalDataScanner = "Pii";

    private const string Value = SensitiveDecoyKind.ValuePlaceholder;

    /// <summary>Every kind a corpus draws from, in the order the two scanners declare their categories.</summary>
    internal static IReadOnlyList<SensitiveDecoyKind> Kinds { get; } =
    [
        new(
            SecretScanner,
            "ProviderToken",
            "digitalocean-pat",
            $"The staging box is back up and its deployment token is {Value} until somebody rotates it.",
            FabricatedCredentials.ProviderToken),
        new(
            SecretScanner,
            "CloudAccessKey",
            "aws-access-token",
            $"Use the access key {Value} for the survey bucket; the secret half is in the vault where it belongs.",
            FabricatedCredentials.CloudAccessKey),
        new(
            SecretScanner,
            "PrivateKey",
            "private-key",
            $"Pasting the beacon host's deployment key here since the vault is still down:\n{Value}",
            FabricatedCredentials.PrivateKey),
        new(
            SecretScanner,
            "JsonWebToken",
            "jwt",
            $"The session the dashboard handed back was {Value} and it expires tonight, so grab the export before then.",
            FabricatedCredentials.JsonWebToken),
        new(
            SecretScanner,
            "ConnectionString",
            "database-connection-uri-credential",
            $"Connect with {Value} and the query from yesterday reproduces the slow scan in about a minute.",
            FabricatedCredentials.ConnectionString),
        new(
            SecretScanner,
            "CredentialUrl",
            "url-credential-query-parameter",
            $"The tide table is at {Value} and the link stops working on Friday.",
            FabricatedCredentials.CredentialUrl),
        new(
            PersonalDataScanner,
            "PaymentCard",
            "CREDIT_CARD",
            $"Finance still has the old harbour card on file, number {Value}, expiring 11/29, which is why the mooring fee went out twice.",
            FabricatedIdentifiers.PaymentCard),
        new(
            PersonalDataScanner,
            "BankAccount",
            "IBAN_CODE",
            $"Send the survey fee to the bank account IBAN {Value} and quote the invoice number in the transfer.",
            FabricatedIdentifiers.BankAccount),

        // Found only by an analyzer asked in Polish, because that is the one language its recogniser is registered
        // for. The sentence around it stays English and stays ASCII all the same: what the recogniser scores on is the
        // identifier's own name standing beside the number, and a sentence in Polish would lose its diacritics in
        // every message this generator encodes as us-ascii or iso-8859-1.
        new(
            PersonalDataScanner,
            "NationalIdentifier",
            "PL_PESEL",
            $"The port pass application still needs the PESEL number, which is {Value}.",
            FabricatedIdentifiers.NationalIdentifier),
        new(
            PersonalDataScanner,
            "NationalIdentifier",
            "US_SSN",
            $"Payroll came back asking for the social security number on file, which is {Value}.",
            FabricatedIdentifiers.SocialSecurityNumber),
        new(
            PersonalDataScanner,
            "IdentityDocument",
            "US_PASSPORT",
            $"The travel desk needs the passport number before the crossing: {Value}, issued last spring.",
            FabricatedIdentifiers.IdentityDocument),
        new(
            PersonalDataScanner,
            "HealthIdentifier",
            "MEDICAL_LICENSE",
            $"The port doctor's medical certificate lists DEA {Value} on every form the crew signs.",
            FabricatedIdentifiers.HealthIdentifier),
    ];

    /// <summary>Plants the decoy whose turn it is.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <param name="ordinal">Which planting this is, counted across the whole corpus.</param>
    /// <returns>The planted decoy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Taken in turn rather than drawn, so a batch large enough to plant twelve decoys carries every kind exactly as
    /// often as every other. Drawing each independently would leave a corpus of fifty messages plausibly missing three
    /// categories, and a developer concluding the scanner misses those. Where in the cycle a corpus starts is drawn
    /// from the seed, so two seeds still differ in which message carries what.
    /// </remarks>
    internal static SensitiveDecoy Plant(Random source, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Kinds[ordinal % Kinds.Count].Plant(source);
    }
}
