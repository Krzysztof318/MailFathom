// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>Declares what the personal-data scanner can look for, so a deployment naming one of them can be judged.</summary>
/// <remarks>
/// Composed from the entity mapping rather than written beside it, so a category an operator can configure is a category
/// the scanner really requests, and an entity that exists is an entity they can suppress by name. Startup reads this to
/// refuse a mistyped name instead of letting the binder drop it, which would leave that category off while the
/// configuration file read as protection that was on.
/// </remarks>
internal sealed class PersonalDataContentCatalog : ISensitiveContentCatalog
{
    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner => SensitiveContentScannerKind.Pii;

    /// <inheritdoc />
    public IReadOnlyList<SensitiveContentCategoryDefinition> Categories { get; } =
    [
        .. PersonalDataCategories.All.Select(category => SensitiveContentCategoryDefinition.Create(
            category,
            PersonalDataCategories.IsDetectedByDefault(category),
            PresidioEntityCorpus.RulesOf(category))),
    ];
}
