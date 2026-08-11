// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.SensitiveContent.Redaction;

/// <summary>Runs every switched-on scanner over a text and replaces what they found, once, for every consumer.</summary>
/// <remarks>
/// <para>
/// This is the single implementation the whole feature turns on. The derived path and the read path both redact through
/// it, so a citation drawn from a redacted chunk lands on the same redacted text when a reader opens the message; two
/// implementations would drift the moment either one gained a rule about ordering, overlap, or truncation.
/// </para>
/// <para>
/// <b>The result is reproducible.</b> For a given text, plan, and set of detector revisions, the redacted text is
/// byte-identical on repeat: scanners run in a fixed order, findings are sorted before they are applied rather than
/// applied in the order they arrived, and overlapping regions merge under a rule that does not depend on which detector
/// answered first.
/// </para>
/// <para>
/// <b>It fails closed.</b> A detector that is absent, slow past the configured budget, broken, or reporting a region
/// outside the text it was handed refuses the operation rather than returning the text unredacted. The one thing that
/// never raises a failure is the analyzed ceiling: text beyond it is dropped from the result rather than passed on, so
/// an over-long input costs the remainder instead of the whole operation.
/// </para>
/// </remarks>
public sealed class SensitiveContentRedactor : IDisposable
{
    private readonly SensitiveContentPlan plan;
    private readonly IReadOnlyList<ISensitiveContentScanner> scanners;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim concurrency;

    /// <summary>Initializes the redactor of a deployment with at least one scanner switched on.</summary>
    /// <param name="plan">What this deployment scans for, and what one scan may spend.</param>
    /// <param name="scanners">Every registered detector, of which the planned ones are used.</param>
    /// <param name="timeProvider">Times the per-call budget and stamps the findings.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SensitiveContentRedactor(
        SensitiveContentPlan plan,
        IEnumerable<ISensitiveContentScanner> scanners,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.plan = plan;
        this.scanners = [.. scanners];
        this.timeProvider = timeProvider;
        this.concurrency = new SemaphoreSlim(
            plan.Bounds.MaximumConcurrentScans,
            plan.Bounds.MaximumConcurrentScans);
    }

    /// <summary>Scans a text and returns it with every detected region replaced by a placeholder.</summary>
    /// <param name="text">The text about to be stored, indexed, or handed out.</param>
    /// <param name="cancellationToken">Cancels the redaction.</param>
    /// <returns>The redacted text, the findings behind it, and how much was dropped at the analyzed ceiling.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<RedactedText> RedactAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var analyzed = text.Length > this.plan.Bounds.MaximumAnalyzedCharacters
            ? text[..AnalyzedLength(text, this.plan.Bounds.MaximumAnalyzedCharacters)]
            : text;

        await this.concurrency.WaitAsync(cancellationToken);

        try
        {
            var findings = await this.CollectFindingsAsync(analyzed, cancellationToken);

            return RedactedText.Create(Apply(analyzed, findings), findings, text.Length - analyzed.Length);
        }
        finally
        {
            this.concurrency.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => this.concurrency.Dispose();

    /// <summary>Finds where to cut an over-long text so the cut does not fall inside a character.</summary>
    /// <remarks>
    /// An emoji, an ideograph beyond the basic plane, and a flag are each two UTF-16 code units, and cutting between
    /// them leaves an unpaired surrogate that no encoder can represent — so the text handed on would be text no
    /// consumer could store or serialize, which is a worse outcome than dropping one more character.
    /// </remarks>
    private static int AnalyzedLength(string text, int ceiling) =>
        char.IsHighSurrogate(text[ceiling - 1]) ? ceiling - 1 : ceiling;

    /// <summary>Applies findings to the analyzed text, merging every overlap so no covered character survives.</summary>
    private static string Apply(string analyzed, IReadOnlyList<SensitiveContentFinding> ordered)
    {
        if (ordered.Count == 0)
        {
            return analyzed;
        }

        var redacted = new StringBuilder(analyzed.Length);
        var cursor = 0;

        foreach (var region in MergeRegions(ordered))
        {
            redacted.Append(analyzed, cursor, region.Span.Start - cursor);
            redacted.Append(SensitiveContentPlaceholder.For(region.Category));
            cursor = region.Span.End;
        }

        redacted.Append(analyzed, cursor, analyzed.Length - cursor);

        return redacted.ToString();
    }

    /// <summary>Collapses overlapping findings into the regions one placeholder each replaces.</summary>
    /// <remarks>
    /// An overlapping finding extends the region it overlaps rather than being dropped, because dropping it would leave
    /// the part of it that reaches past the first region in the redacted text. The category is the first region's,
    /// which the ordering makes reproducible rather than a matter of which detector answered first.
    /// </remarks>
    private static List<RedactedRegion> MergeRegions(IReadOnlyList<SensitiveContentFinding> ordered)
    {
        var regions = new List<RedactedRegion>(ordered.Count);

        foreach (var finding in ordered)
        {
            if (regions.Count > 0 && regions[^1].Span.Overlaps(finding.Span))
            {
                regions[^1] = regions[^1] with { Span = regions[^1].Span.CoverWith(finding.Span) };

                continue;
            }

            regions.Add(new RedactedRegion(finding.Span, finding.Category));
        }

        return regions;
    }

    private async Task<IReadOnlyList<SensitiveContentFinding>> CollectFindingsAsync(
        string analyzed,
        CancellationToken cancellationToken)
    {
        var findings = new List<SensitiveContentFinding>();

        // Sequentially and in plan order: the concurrency bound is about how many redactions run at once, and running
        // the two scanners against one text in parallel would spend two permits on one caller's work.
        foreach (var scannerPlan in this.plan.Scanners)
        {
            var scanner = this.scanners.FirstOrDefault(candidate => candidate.Scanner == scannerPlan.Scanner)
                ?? throw SensitiveContentScannerUnavailableException.NotRegistered(scannerPlan.Scanner);

            findings.AddRange(await this.ScanAsync(scanner, analyzed, cancellationToken));
        }

        return
        [
            .. findings
                .OrderBy(finding => finding.Span.Start)
                .ThenByDescending(finding => finding.Span.Length)
                .ThenBy(finding => finding.Category.Name, StringComparer.Ordinal)
                .ThenBy(finding => finding.Rule.Name, StringComparer.Ordinal)
                .ThenBy(finding => finding.Detector.Name, StringComparer.Ordinal),
        ];
    }

    private async Task<IReadOnlyList<SensitiveContentFinding>> ScanAsync(
        ISensitiveContentScanner scanner,
        string analyzed,
        CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(this.plan.Bounds.ScanTimeout, this.timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);

        IReadOnlyList<SensitiveContentFinding> findings;

        try
        {
            findings = await scanner.ScanAsync(analyzed, linked.Token);
        }
        // A caller that cancelled receives its own cancellation, because a shutting-down host and a detector that
        // stopped answering are different facts about the run, and only the second one says anything about the text.
        catch (OperationCanceledException) when (
            budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw SensitiveContentScannerUnavailableException.DidNotAnswerInTime(
                scanner.Scanner,
                this.plan.Bounds.ScanTimeout);
        }
        catch (OperationCanceledException cancelledElsewhere) when (!cancellationToken.IsCancellationRequested)
        {
            // Nobody this method can see cancelled, so the scanner cancelled itself. Reporting it as a timeout would
            // name a budget that was never spent; it is a scanner that did not answer, like any other.
            throw SensitiveContentScannerUnavailableException.Failed(scanner.Scanner, cancelledElsewhere);
        }
        catch (SensitiveContentScannerUnavailableException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw SensitiveContentScannerUnavailableException.Failed(scanner.Scanner, failure);
        }

        return VerifyWithin(scanner, analyzed, findings);
    }

    /// <summary>Refuses findings a scanner reported outside the text it was handed.</summary>
    /// <remarks>
    /// A span past the end of the analyzed text is a broken adapter rather than a detection, and clamping it would
    /// redact a region nothing found while leaving whatever the detector meant untouched. Refusing keeps the failure
    /// where the fault is.
    /// </remarks>
    private static IReadOnlyList<SensitiveContentFinding> VerifyWithin(
        ISensitiveContentScanner scanner,
        string analyzed,
        IReadOnlyList<SensitiveContentFinding> findings)
    {
        if (findings is null)
        {
            throw SensitiveContentScannerUnavailableException.Failed(
                scanner.Scanner,
                new InvalidOperationException("The scanner returned no finding list at all."));
        }

        var stray = findings.FirstOrDefault(finding => finding.Span.End > analyzed.Length);

        if (stray is not null)
        {
            throw SensitiveContentScannerUnavailableException.Failed(
                scanner.Scanner,
                new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "The scanner reported a finding ending at {0} in a text of {1} characters.",
                    stray.Span.End,
                    analyzed.Length)));
        }

        return findings;
    }

    private readonly record struct RedactedRegion(SensitiveContentSpan Span, SensitiveContentCategory Category);
}
