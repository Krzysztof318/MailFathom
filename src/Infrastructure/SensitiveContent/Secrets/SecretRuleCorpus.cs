// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using System.Globalization;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using Microsoft.Security.Utilities;

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>Everything the secret scanner can look for, assembled from the three places its rules come from.</summary>
/// <remarks>
/// <para>
/// The detection engine ships a corpus oriented on Microsoft credential formats, the gitleaks rule data supplies the
/// third-party providers a mailbox actually receives, and MailFathom writes the few shapes neither covers. All three
/// arrive here as one list, and <see cref="Detector" /> names one identity and one revision for whichever of them
/// matched: an operator diagnosing a false positive should not have to learn which corpus a rule came from before they
/// can suppress it.
/// </para>
/// <para>
/// The revision moves whenever any of the three does, which is what makes a redaction reproducible against a stated
/// corpus rather than against whatever this build happened to carry. It carries a fourth part beside them: the gitleaks
/// half is named by the release it was taken from <em>and</em> by
/// <see cref="GitleaksSecretRules.TransformationRevision" />, because what a rule matches here is that release read
/// through the transformations named there, and either one moving is a different corpus.
/// </para>
/// </remarks>
internal static class SecretRuleCorpus
{
    /// <summary>The rule a finding is reported under when the engine names a pattern this catalog does not declare.</summary>
    /// <remarks>
    /// The engine's Microsoft corpus refines the name of a match: one expression recognises a family of keys and then
    /// reads the provider signature inside the match to say which product issued it. Nearly every name that reaches is
    /// already declared, because the whole family is declared, but the mapping is the engine's and a future release of
    /// it may name a product this build does not. Reporting such a match under a declared rule keeps the finding rather
    /// than dropping it, keeps the category right — every refining expression is a provider-issued credential — and
    /// leaves an operator a name they can suppress.
    /// </remarks>
    public static SensitiveContentRule UnnamedProviderCredential { get; } =
        SensitiveContentRule.Create(SecretCategories.ProviderToken, "unnamed-provider-credential");

    /// <summary>Every rule the scanner can look for, whichever corpus supplied it.</summary>
    public static IReadOnlyList<SecretRuleDefinition> Rules { get; } =
    [
        .. GitleaksSecretRules.Rules,
        .. MailFathomSecretRules.Rules,
        .. AdoptedRules(),
    ];

    /// <summary>The detector identity and corpus revision every finding of this scanner carries.</summary>
    public static SensitiveContentDetector Detector { get; } = SensitiveContentDetector.Create(
        "mailfathom-secrets",
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}+gitleaks.{1}.{2}+own.{3}",
            SecretMasker.Version,
            GitleaksSecretRules.CorpusRevision,
            GitleaksSecretRules.TransformationRevision,
            MailFathomSecretRules.CorpusRevision));

    /// <summary>Every rule the scanner can look for, indexed by the name a detection reports.</summary>
    public static FrozenDictionary<string, SecretRuleDefinition> RulesByName { get; } =
        Rules.ToFrozenDictionary(rule => rule.Rule.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<SecretRuleDefinition> AdoptedRules() =>
    [
        .. WellKnownRegexPatterns.PreciselyClassifiedSecurityKeys
            .Select(pattern => SecretRuleDefinition.Adopt(SecretCategories.ProviderToken, pattern)),
        .. WellKnownRegexPatterns.UnclassifiedPotentialSecurityKeys
            .Select(pattern => SecretRuleDefinition.Adopt(UnclassifiedCategoryOf(pattern.Name), pattern)),
    ];

    /// <summary>Finds the category one of the engine's unclassified patterns belongs to.</summary>
    /// <remarks>
    /// The list the engine calls its unclassified potential security keys is mostly the recall layer this scanner
    /// exposes as <see cref="SecretCategories.HighEntropyString" />: a base64 or hexadecimal run of a given length,
    /// which is a credential often enough to look for and ordinary data often enough to be off by default. Three of its
    /// entries are not that at all — they recognise a shape — so each is named here and everything else falls through
    /// to the entropy layer, which is what keeps a future addition to that list from arriving switched on.
    /// </remarks>
    private static SensitiveContentCategory UnclassifiedCategoryOf(string name) => name switch
    {
        "UnclassifiedJwt" => SecretCategories.JsonWebToken,
        "UrlCredentials" => SecretCategories.CredentialUrl,
        "Pkcs12CertificatePrivateKeyBundle" => SecretCategories.PrivateKey,
        _ => SecretCategories.HighEntropyString,
    };
}
