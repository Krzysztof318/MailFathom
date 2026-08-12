// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Infrastructure.SensitiveContent;

/// <summary>Declares what the secret scanner can look for, so a deployment naming one of them can be judged.</summary>
/// <remarks>
/// The declaration is composed from the corpus rather than written beside it, so a rule that exists is a rule an
/// operator can suppress and a rule that does not exist fails startup by name. That also means the catalog cannot drift
/// from what actually runs: both are the same list read twice.
/// </remarks>
internal sealed class SecretContentCatalog : ISensitiveContentCatalog
{
    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner => SensitiveContentScannerKind.Secrets;

    /// <inheritdoc />
    public IReadOnlyList<SensitiveContentCategoryDefinition> Categories { get; } =
    [
        .. SecretCategories.All.Select(category => SensitiveContentCategoryDefinition.Create(
            category,
            SecretCategories.IsDetectedByDefault(category),
            RulesOf(category))),
    ];

    private static IReadOnlyList<SensitiveContentRule> RulesOf(SensitiveContentCategory category)
    {
        var declared = SecretRuleCorpus.Rules
            .Select(definition => definition.Rule)
            .Where(rule => rule.Category == category);

        // The rule an unnamed refinement is reported under is declared beside the ones with an expression behind them,
        // because a name an operator cannot see in the catalog is a name they cannot suppress: startup refuses a
        // suppression naming a rule no category holds.
        return category == SecretCategories.ProviderToken
            ? [.. declared, SecretRuleCorpus.UnnamedProviderCredential]
            : [.. declared];
    }
}
