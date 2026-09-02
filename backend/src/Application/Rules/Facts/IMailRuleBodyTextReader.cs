// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Facts;

/// <summary>Reads the extracted body text of the one email a rule set is being evaluated for.</summary>
/// <remarks>
/// <para>
/// The only fact whose resolution reaches storage, and the reason the fact surface resolves lazily at all: a rule set
/// whose conditions never name <see cref="MailRuleFact.BodyText" /> must cost no read, and a rule set that names it in
/// several conditions must cost one. <see cref="MailRuleFacts" /> owns both halves of that, so an implementation of this
/// port answers for its email and holds no cache of its own.
/// </para>
/// <para>
/// Bound to a single email by whoever constructs it, which is what keeps the identity of that email out of the fact
/// surface: nothing a condition can write names an email, so no condition can reach a message other than the one being
/// evaluated.
/// </para>
/// </remarks>
public interface IMailRuleBodyTextReader
{
    /// <summary>Reads the text extracted from the email's body.</summary>
    /// <param name="cancellationToken">Cancels the read, which the evaluation timeout also reaches through.</param>
    /// <returns>The extracted text, or <see langword="null" /> when no extraction has produced any for this email.</returns>
    Task<string?> ReadBodyTextAsync(CancellationToken cancellationToken);
}
