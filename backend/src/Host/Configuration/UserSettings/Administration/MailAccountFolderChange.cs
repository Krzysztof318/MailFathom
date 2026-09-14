// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What one folder change made of a mail account's settings.</summary>
/// <param name="Candidate">The settings the change would leave, or <see langword="null" /> where it composed none.</param>
/// <param name="Refusal">The rule the change broke, or <see langword="null" /> where it broke none.</param>
/// <remarks>
/// Three answers rather than two, and the pair tells them apart: settings the caller goes on to have judged, a rule
/// this surface fixes and therefore a sentence to answer with, or neither — which is a change naming a folder the
/// account does not declare, and is the caller's own sentence rather than one composed here.
/// </remarks>
internal readonly record struct MailAccountFolderChange(string? Candidate, string? Refusal)
{
    /// <summary>The answer to a change naming a folder the account declares none of.</summary>
    internal static MailAccountFolderChange NoSuchFolder => default;

    /// <summary>Answers the settings a change composed.</summary>
    /// <param name="candidate">The settings the change would leave.</param>
    /// <returns>The composed change.</returns>
    internal static MailAccountFolderChange Composed(string candidate) => new(candidate, Refusal: null);

    /// <summary>Answers the rule a change broke.</summary>
    /// <param name="refusal">The sentence naming the rule.</param>
    /// <returns>The refused change.</returns>
    internal static MailAccountFolderChange Refused(string refusal) => new(Candidate: null, refusal);
}
