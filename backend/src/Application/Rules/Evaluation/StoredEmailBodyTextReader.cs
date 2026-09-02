// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Binds the store's body-text read to the one email a rule set is being evaluated for.</summary>
/// <remarks>
/// The port it implements is deliberately email-less, so a condition cannot name a message other than the one in front
/// of it. This is where that binding is made: one instance per email, constructed by the pass, holding no cache of its
/// own because the fact surface already resolves a fact once per email.
/// </remarks>
internal sealed class StoredEmailBodyTextReader : IMailRuleBodyTextReader
{
    private readonly IMailRuleEvaluationStore store;
    private readonly StoredEmailId storedEmailId;

    internal StoredEmailBodyTextReader(IMailRuleEvaluationStore store, StoredEmailId storedEmailId)
    {
        this.store = store;
        this.storedEmailId = storedEmailId;
    }

    /// <inheritdoc />
    public Task<string?> ReadBodyTextAsync(CancellationToken cancellationToken) =>
        this.store.ReadExtractedBodyTextAsync(this.storedEmailId, cancellationToken);
}
