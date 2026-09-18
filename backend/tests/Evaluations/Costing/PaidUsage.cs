// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Costing;

/// <summary>What one client was actually sent, and what the provider charged for it.</summary>
/// <param name="Calls">How many requests reached the provider, which an answer served from the cache does not add to.</param>
/// <param name="InputTokens">The tokens those requests carried, counted by the provider's own tokenizer.</param>
/// <param name="OutputTokens">The tokens they were answered with.</param>
/// <param name="Cost">What the provider charged, in dollars, or <see langword="null" /> where no answer carried a charge.</param>
internal readonly record struct PaidUsage(int Calls, long InputTokens, long OutputTokens, decimal? Cost)
{
    /// <summary>Gets whether anything reached a provider at all.</summary>
    public bool IsFree => this.Calls is 0;
}
