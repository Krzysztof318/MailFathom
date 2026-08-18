// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports what guarding each egress point found, refused, and cost.</summary>
/// <remarks>
/// <para>
/// The egress point is on every instrument because it is what an operator acts on. "Something was redacted" says
/// nothing; a scanner finding credentials in retrieved extracts and nothing in subjects, or adding two seconds to a
/// listing and nothing to an embedding call, is where a category list or a bound gets changed.
/// </para>
/// <para>
/// Nothing recorded here is mail or derived from it. The tags are an egress point, a category name, and a scanner name,
/// all three of them MailFathom's own closed sets, and the values are counts and durations — never a rule's match, a
/// position, a message identity, or any part of what was found, each of which would put the credential in the
/// telemetry written to prove it never left.
/// </para>
/// </remarks>
public sealed class SensitiveContentEgressTelemetry : ISensitiveContentEgressTelemetry
{
    /// <summary>The name one guarded operation opens its span under, beneath whatever asked for the payload.</summary>
    /// <remarks>
    /// One span for the operation rather than one per text, because a body, a subject, and a display name are what a
    /// single read publishes and their sum is what the caller waited for. The instruments beside it stay per value,
    /// which is the level a category list or a bound is decided at.
    /// </remarks>
    internal const string GuardedOperationSpanName = "scan_sensitive_content";

    private const string EgressPointTagName = "mailfathom.sensitive_content.egress_point";
    private const string CategoryTagName = "mailfathom.sensitive_content.category";
    private const string ScannerTagName = "mailfathom.sensitive_content.scanner";

    /// <summary>How many texts one guarded operation scanned, which is what its duration has to be read against.</summary>
    private const string GuardedTextCountTagName = "mailfathom.sensitive_content.texts";

    /// <summary>How the operation ended, in the two words that separate a scan that answered from one that could not.</summary>
    private const string OutcomeTagName = "mailfathom.sensitive_content.outcome";

    private const string SucceededOutcomeName = "succeeded";
    private const string RefusedOutcomeName = "refused";

    private readonly Counter<long> guardedTextCount;
    private readonly Counter<long> findingCount;
    private readonly Counter<long> omittedCharacterCount;
    private readonly Counter<long> refusalCount;
    private readonly Histogram<double> scanDuration;

    /// <summary>Initializes the instruments every guarded egress reports through.</summary>
    public SensitiveContentEgressTelemetry()
    {
        this.guardedTextCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.guarded",
            unit: "{text}",
            description: "Texts scanned before they crossed out of the deployment, by egress point.");
        this.findingCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.findings",
            unit: "{finding}",
            description: "Sensitive-content findings redacted before egress, by egress point and category.");
        this.omittedCharacterCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.omitted",
            unit: "{character}",
            description: "Characters dropped at the analyzed ceiling rather than handed on unscanned, by egress point.");
        this.refusalCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.sensitive_content.refusals",
            unit: "{refusal}",
            description: "Egress operations refused because a switched-on scanner could not answer, by egress point and scanner.");
        this.scanDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.sensitive_content.scan.duration",
            unit: "s",
            description: "How long scanning added to one guarded operation, by egress point.");
    }

    /// <inheritdoc />
    public void RecordGuarded(SensitiveContentEgressPoint egressPoint, RedactedText redacted, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        var tags = new TagList { { EgressPointTagName, TagOf(egressPoint) } };

        this.guardedTextCount.Add(1, tags);
        this.scanDuration.Record(elapsed.TotalSeconds, tags);

        // Recorded per category rather than as one total, because which kind of material a mailbox is producing is what
        // decides whether a category list is right — and a total says only that the feature is switched on.
        foreach (var category in redacted.Findings.GroupBy(finding => finding.Category.Name, StringComparer.Ordinal))
        {
            this.findingCount.Add(
                category.Count(),
                new TagList
                {
                    { EgressPointTagName, TagOf(egressPoint) },
                    { CategoryTagName, category.Key },
                });
        }

        // Only when the ceiling actually cut something. A zero on every guarded text would make the series say the
        // ceiling is in play on ordinary mail, which is exactly the question this instrument exists to answer.
        if (redacted.OmittedCharacterCount > 0)
        {
            this.omittedCharacterCount.Add(redacted.OmittedCharacterCount, tags);
        }
    }

    /// <inheritdoc />
    public void RecordRefused(SensitiveContentEgressPoint egressPoint, SensitiveContentScannerKind scanner) =>
        this.refusalCount.Add(
            1,
            new TagList
            {
                { EgressPointTagName, TagOf(egressPoint) },
                { ScannerTagName, TagOf(scanner) },
            });

    /// <inheritdoc />
    public ISensitiveContentGuardScope BeginGuardedOperation(SensitiveContentEgressPoint egressPoint)
    {
        var activity = Telemetry.ActivitySource.StartActivity(GuardedOperationSpanName);
        activity?.SetTag(EgressPointTagName, TagOf(egressPoint));

        return new GuardedOperation(activity);
    }

    /// <summary>Names an egress point as a tag value.</summary>
    /// <remarks>
    /// A closed mapping rather than the member's own name, for the reason every published mapping here is closed: the
    /// tag value is what a dashboard and an alert are written against, so a member added without one has to fail rather
    /// than silently rename a series.
    /// </remarks>
    private static string TagOf(SensitiveContentEgressPoint egressPoint) => egressPoint switch
    {
        SensitiveContentEgressPoint.ChatPrompt => "chat_prompt",
        SensitiveContentEgressPoint.HostedEmbeddingInput => "hosted_embedding_input",
        SensitiveContentEgressPoint.McpSnippet => "mcp_snippet",
        SensitiveContentEgressPoint.McpEmailContent => "mcp_email_content",
        _ => "unknown",
    };

    private static string TagOf(SensitiveContentScannerKind scanner) => scanner switch
    {
        SensitiveContentScannerKind.Secrets => "secrets",
        SensitiveContentScannerKind.Pii => "pii",
        _ => "unknown",
    };

    /// <summary>Carries one guarded operation from the span that opens it to the count and the ending that close it.</summary>
    /// <remarks>
    /// The count is written at the end rather than as each text arrives, because a tag set repeatedly is a tag rewritten
    /// repeatedly on a span nothing has read yet. A refusal is an ending rather than an error status for the reason the
    /// refusal is not a defect: a scanner that could not answer stopped an egress on purpose, and the operation the
    /// caller sees fails with an error code of its own.
    /// </remarks>
    private sealed class GuardedOperation(Activity? activity) : ISensitiveContentGuardScope
    {
        private int guardedTextCount;
        private bool refused;

        // Counted atomically because the operation is the unit and the values inside it are not: a consumer that
        // guards the fields of one payload concurrently would otherwise publish a count lower than what it scanned.
        public void TextGuarded() => Interlocked.Increment(ref this.guardedTextCount);

        public void Refused() => this.refused = true;

        public void Dispose()
        {
            if (activity is null)
            {
                return;
            }

            activity.SetTag(GuardedTextCountTagName, Volatile.Read(ref this.guardedTextCount));
            activity.SetTag(OutcomeTagName, this.refused ? RefusedOutcomeName : SucceededOutcomeName);
            activity.SetStatus(ActivityStatusCode.Ok);
            activity.Dispose();
        }
    }
}
