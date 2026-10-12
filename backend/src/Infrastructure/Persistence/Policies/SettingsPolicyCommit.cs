// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>What one attempt to commit a settings policy did.</summary>
/// <param name="Result">How the attempt ended.</param>
/// <param name="Version">The version the commit produced; <see langword="null" /> for every other ending.</param>
/// <param name="GovernedRecords">How many records the write would have judged, user records and mail accounts together, where the attempt counted them: the figure a write past the bound is refused with, and the total a refusal over invalid records is out of.</param>
/// <param name="RecordsInTheWay">The first records that refuse the policy, up to <see cref="MaximumRecordsNamed" />, each with what refuses it; empty for every ending but <see cref="SettingsPolicyCommitResult.RecordsLeftInvalid" />.</param>
/// <param name="RecordsLeftInvalid">How many records refuse the policy in all, of which <paramref name="RecordsInTheWay" /> names the first.</param>
public sealed record SettingsPolicyCommit(
    SettingsPolicyCommitResult Result,
    long? Version = null,
    int GovernedRecords = 0,
    IReadOnlyList<SettingsPolicyRecordInTheWay>? RecordsInTheWay = null,
    int RecordsLeftInvalid = 0)
{
    /// <summary>Gets the first records that refuse the policy, which is empty for every ending but <see cref="SettingsPolicyCommitResult.RecordsLeftInvalid" />.</summary>
    public IReadOnlyList<SettingsPolicyRecordInTheWay> RecordsInTheWay { get; init; } = RecordsInTheWay ?? [];

    /// <summary>The most records one refusal names, so that an answer about a policy governing thousands stays one a person reads.</summary>
    public const int MaximumRecordsNamed = 10;

    /// <summary>Describes a write refused over the records in its way.</summary>
    /// <param name="governed">How many records the write judged in all.</param>
    /// <param name="inTheWay">Every record that refuses it, users first.</param>
    /// <returns>The refusal naming the first of them, or <see langword="null" /> where none refuses the write.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inTheWay" /> is <see langword="null" />.</exception>
    public static SettingsPolicyCommit? RefusedOver(int governed, IReadOnlyList<SettingsPolicyRecordInTheWay> inTheWay)
    {
        ArgumentNullException.ThrowIfNull(inTheWay);

        return inTheWay.Count == 0
            ? null
            : new SettingsPolicyCommit(
                SettingsPolicyCommitResult.RecordsLeftInvalid,
                GovernedRecords: governed,
                RecordsInTheWay: [.. inTheWay.Take(MaximumRecordsNamed)],
                RecordsLeftInvalid: inTheWay.Count);
    }
}
