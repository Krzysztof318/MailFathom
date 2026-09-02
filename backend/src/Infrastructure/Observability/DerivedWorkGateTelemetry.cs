// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Spam.Gating;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what ordering derived work behind classification withheld, held, and let through.</summary>
/// <remarks>
/// <para>
/// The admission is on the instrument because withholding is otherwise invisible: work that is never started leaves no
/// trace, so a gate holding a mailbox and a mailbox producing nothing read identically. What an operator acts on is
/// which of the five answers is rising — junk is the feature working, a rising wait is a classification backlog, and a
/// rising release is the deployment saying its scanner is not answering and it is indexing without one.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The one tag is an admission, which is MailFathom's own closed set,
/// and the values are counts of decisions and of passages. No message identity, folder, address, score, or verdict
/// detail reaches an instrument from the gate.
/// </para>
/// </remarks>
public sealed class DerivedWorkGateTelemetry : IDerivedWorkGateTelemetry
{
    private const string AdmissionTagName = "mailfathom.spam.admission";

    private readonly Counter<long> admissionCount;
    private readonly Counter<long> discardedPassageCount;

    /// <summary>Initializes the instruments the gate reports through.</summary>
    public DerivedWorkGateTelemetry()
    {
        this.admissionCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.spam.derived_work.admissions",
            unit: "{decision}",
            description: "Decisions the classification gate reached about running derived work for a message, by admission.");
        this.discardedPassageCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.spam.derived_work.discarded",
            unit: "{passage}",
            description: "Passages, and the vectors hanging off them, removed from mail a classification called junk after they had been derived.");
    }

    /// <inheritdoc />
    public void RecordAdmission(DerivedWorkAdmission admission) =>
        this.admissionCount.Add(1, new TagList { { AdmissionTagName, TagOf(admission) } });

    /// <inheritdoc />
    public void RecordDiscardedPassages(int passageCount)
    {
        if (passageCount > 0)
        {
            this.discardedPassageCount.Add(passageCount);
        }
    }

    private static string TagOf(DerivedWorkAdmission admission) => admission switch
    {
        DerivedWorkAdmission.WithheldAsJunk => "withheld_as_junk",
        DerivedWorkAdmission.AwaitingClassification => "awaiting_classification",
        DerivedWorkAdmission.ReleasedAsUnclassifiable => "released_as_unclassifiable",
        DerivedWorkAdmission.ReleasedAfterWaiting => "released_after_waiting",
        _ => "admitted",
    };
}
