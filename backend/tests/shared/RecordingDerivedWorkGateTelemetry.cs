// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;

namespace MailFathom.TestSupport;

/// <summary>Keeps what the classification gate reported, so a test can assert the decision it could not otherwise see.</summary>
/// <remarks>
/// Withholding leaves no trace by construction — the work is never started — so the instrument is the only place a test
/// can tell a held message from a mailbox that produced nothing.
/// </remarks>
internal sealed class RecordingDerivedWorkGateTelemetry : IDerivedWorkGateTelemetry
{
    private readonly List<DerivedWorkAdmission> admissions = [];
    private readonly List<int> discardedPassageCounts = [];

    /// <summary>Gets every admission recorded, in the order the decisions were reached.</summary>
    public IReadOnlyList<DerivedWorkAdmission> Admissions => this.admissions;

    /// <summary>Gets every passage count a junk verdict reported removing, in order.</summary>
    public IReadOnlyList<int> DiscardedPassageCounts => this.discardedPassageCounts;

    /// <inheritdoc />
    public void RecordAdmission(DerivedWorkAdmission admission) => this.admissions.Add(admission);

    /// <inheritdoc />
    public void RecordDiscardedPassages(int passageCount) => this.discardedPassageCounts.Add(passageCount);
}
