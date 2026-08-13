// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what redacting the derived writes found, refused, and cost.</summary>
/// <remarks>
/// <para>
/// Instruments of its own rather than a fourth egress point on the guarded-egress ones, because the two measure work
/// with different shapes. An egress figure is latency somebody is waiting on; this one is throughput a background walk
/// is spending, and a deployment that has just switched a scanner on wants to read the second without the first moving
/// underneath it.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. A category name and a scanner name are MailFathom's own closed
/// sets, and the values are counts and durations — never a match, a position, or a message identity.
/// </para>
/// </remarks>
public sealed class SensitiveContentDerivationTelemetry : ISensitiveContentDerivationTelemetry
{
    private const string CategoryTagName = "mailfathom.sensitive_content.category";
    private const string ScannerTagName = "mailfathom.sensitive_content.scanner";

    private readonly Counter<long> derivedTextCount;
    private readonly Counter<long> findingCount;
    private readonly Counter<long> omittedCharacterCount;
    private readonly Counter<long> refusalCount;
    private readonly Histogram<double> scanDuration;

    /// <summary>Initializes the instruments every redacted derivation reports through.</summary>
    public SensitiveContentDerivationTelemetry()
    {
        this.derivedTextCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.derivation.redacted",
            unit: "{text}",
            description: "Texts scanned before they were written into the derived store.");
        this.findingCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.derivation.findings",
            unit: "{finding}",
            description: "Sensitive-content findings redacted before a derived write, by category.");
        this.omittedCharacterCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.derivation.omitted",
            unit: "{character}",
            description: "Characters dropped at the analyzed ceiling rather than derived unscanned.");
        this.refusalCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.derivation.refusals",
            unit: "{refusal}",
            description: "Derived writes refused because a switched-on scanner could not answer, by scanner.");
        this.scanDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.sensitive_content.derivation.duration",
            unit: "s",
            description: "How long scanning added to one derived write.");
    }

    /// <inheritdoc />
    public void RecordDerived(RedactedText redacted, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        this.derivedTextCount.Add(1);
        this.scanDuration.Record(elapsed.TotalSeconds);

        // Per category rather than as one total, because which kind of material a mailbox is producing is what decides
        // whether a category list is right — and a total says only that the feature is switched on.
        foreach (var category in redacted.Findings.GroupBy(finding => finding.Category.Name, StringComparer.Ordinal))
        {
            this.findingCount.Add(category.Count(), new TagList { { CategoryTagName, category.Key } });
        }

        // Only when the ceiling actually cut something. A zero on every derived write would make the series say the
        // ceiling is in play on ordinary mail, which is exactly the question this instrument exists to answer — and here
        // the cut is worth more attention than at an egress point, because what it drops is dropped from the index for
        // as long as the message stays derived under this configuration.
        if (redacted.OmittedCharacterCount > 0)
        {
            this.omittedCharacterCount.Add(redacted.OmittedCharacterCount);
        }
    }

    /// <inheritdoc />
    public void RecordRefused(SensitiveContentScannerKind scanner) =>
        this.refusalCount.Add(1, new TagList { { ScannerTagName, TagOf(scanner) } });

    /// <summary>Names a scanner as a tag value.</summary>
    /// <remarks>
    /// A closed mapping rather than the member's own name, for the reason every published mapping here is closed: the
    /// tag value is what a dashboard and an alert are written against, so a member added without one has to fail rather
    /// than silently rename a series.
    /// </remarks>
    private static string TagOf(SensitiveContentScannerKind scanner) => scanner switch
    {
        SensitiveContentScannerKind.Secrets => "secrets",
        SensitiveContentScannerKind.Pii => "pii",
        _ => "unknown",
    };
}
