// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Administration;

/// <summary>What one reading of the deployment's settings answers with, or the count that stopped it answering.</summary>
/// <param name="Settings">The settings the prefix matched, ordered by path, and empty where the prefix was too broad.</param>
/// <param name="MatchedCount">How many settings the prefix matched, which is what a refusal reports and what an answer's own count agrees with.</param>
/// <remarks>
/// The bound is reported as a count rather than as a truncated answer, because a truncated configuration reading is
/// the one shape an operator cannot act on: they would narrow a prefix without knowing whether what they were looking
/// for had been cut. Saying how many matched turns it into an instruction.
/// </remarks>
internal sealed record SettingsReading(IReadOnlyList<EffectiveSetting> Settings, int MatchedCount)
{
    /// <summary>Gets whether the prefix matched more settings than one reading answers with.</summary>
    public bool IsTooBroad => this.MatchedCount > EffectiveSettingsReader.MaximumSettings;

    /// <summary>Reports a reading that answered.</summary>
    /// <param name="settings">The settings, ordered by path.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    internal static SettingsReading Of(IReadOnlyList<EffectiveSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SettingsReading(settings, settings.Count);
    }

    /// <summary>Reports a prefix that matched more settings than one reading answers with.</summary>
    /// <param name="matchedCount">How many it matched.</param>
    /// <returns>The reading, carrying no settings.</returns>
    internal static SettingsReading TooBroad(int matchedCount) => new([], matchedCount);
}
