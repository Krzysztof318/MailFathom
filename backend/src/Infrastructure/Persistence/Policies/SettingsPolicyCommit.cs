// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>What one attempt to commit a settings policy did.</summary>
/// <param name="Result">How the attempt ended.</param>
/// <param name="Version">The version the commit produced; <see langword="null" /> for every other ending.</param>
/// <param name="GovernedRecords">How many user records the policy governs, where the attempt counted them: the figure a write past the bound is refused with, and the total a refusal over invalid records is out of.</param>
/// <param name="RecordsInTheWay">The first records the policy would have left unservable, up to <see cref="MaximumRecordsNamed" />, each with what refuses it; empty for every ending but <see cref="SettingsPolicyCommitResult.RecordsLeftInvalid" />.</param>
/// <param name="RecordsLeftInvalid">How many records the policy would have left unservable in all, of which <paramref name="RecordsInTheWay" /> names the first.</param>
public sealed record SettingsPolicyCommit(
    SettingsPolicyCommitResult Result,
    long? Version = null,
    int GovernedRecords = 0,
    IReadOnlyList<SettingsPolicyRecordInTheWay>? RecordsInTheWay = null,
    int RecordsLeftInvalid = 0)
{
    /// <summary>Gets the first records the policy would have left unservable, which is empty for every ending but <see cref="SettingsPolicyCommitResult.RecordsLeftInvalid" />.</summary>
    public IReadOnlyList<SettingsPolicyRecordInTheWay> RecordsInTheWay { get; init; } = RecordsInTheWay ?? [];

    /// <summary>The most records one refusal names, so that an answer about a policy governing thousands stays one a person reads.</summary>
    public const int MaximumRecordsNamed = 10;
}
