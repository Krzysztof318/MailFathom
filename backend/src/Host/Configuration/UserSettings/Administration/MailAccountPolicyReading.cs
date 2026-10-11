// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Policies;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One mail account as the settings policies governing it resolve it, for whoever read it.</summary>
/// <param name="Properties">Every property of the account, in path order, each with its effective value, the step of the resolution order that supplied it, and who holds it; empty where <paramref name="Unresolved" /> says why the account could not be resolved.</param>
/// <param name="Changeable">The paths of the properties whoever read the account may change: no settings policy holds the property against them, and a write of theirs reaches it.</param>
/// <param name="Unresolved">The sentence saying which settings policy this build does not read, or <see langword="null" /> where the account was resolved.</param>
/// <remarks>
/// Both halves of what makes a property changeable are folded in, for the reason a user's record folds them: a read
/// needs less than a write does, so a reader told a property was theirs on the policy alone would be offered a
/// control whose change is then refused.
/// </remarks>
internal sealed record MailAccountPolicyReading(
    IReadOnlyList<ResolvedProperty> Properties,
    IReadOnlyList<string> Changeable,
    string? Unresolved);
