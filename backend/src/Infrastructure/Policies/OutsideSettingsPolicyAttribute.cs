// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Policies;

/// <summary>Marks a property of a governed record as one no settings policy says anything about.</summary>
/// <param name="reason">Why, as the words that complete a refusal opening with the property's path and <c>is</c>.</param>
/// <remarks>
/// A policy governs the properties of a record and never which records exist or how one is identified, so the few
/// properties that are not settings at all — the list of a user's mail accounts, the identifier this deployment
/// generates for one — are stated to be outside it rather than left for a reader to infer. A policy naming one is
/// refused with the reason given here, which is what tells whoever wrote it where the statement belongs instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OutsideSettingsPolicyAttribute(string reason) : Attribute
{
    /// <summary>Gets why no policy governs the marked property.</summary>
    public string Reason { get; } = reason;
}
