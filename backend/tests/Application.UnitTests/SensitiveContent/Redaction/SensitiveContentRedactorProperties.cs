// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using CsCheck;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Redaction;

/// <summary>States the two guarantees redaction exists for: what was found does not leave, and nothing else is lost.</summary>
/// <remarks>
/// <para>
/// The examples beside this file cover the shapes somebody thought of — one region, two overlapping ones, a text cut at
/// the analyzed ceiling. These state the rules those examples are cases of, over texts that put detected values against
/// each other, against the ceiling, and against a surrogate pair the cut must not fall inside.
/// </para>
/// <para>
/// Two values are detected rather than one, and the second is often built to start inside the first, because
/// overlapping findings are where over-hiding is decided. The two properties are not one claim stated twice: the first
/// says a detected value is unreadable, which a region covering only part of an overlap still satisfies, and the second
/// says every character a finding covered is gone, which is what makes the covering rule assertable at all.
/// </para>
/// <para>
/// The values are drawn from digits alone, and that is what makes the first claim exact rather than nearly true. A
/// placeholder carries no digit, so no occurrence of a value can be assembled out of one — the only text left around a
/// placeholder is text the scanners already read, where an occurrence would have been found and replaced. A value
/// sharing characters with <c>[redacted:…]</c> would fail for the placeholder's spelling rather than for anything
/// redaction did, and for the same reason nothing a text is built from carries a bracket or a colon.
/// </para>
/// </remarks>
public sealed class SensitiveContentRedactorProperties
{
    /// <summary>How many inputs each property here draws, kept lower because each one runs two asynchronous scans.</summary>
    private const int Iterations = 200;

    /// <summary>
    /// An analyzed ceiling far below the deployed one, so that a generated text reaches it often rather than never and
    /// the cut that keeps a surrogate pair whole is part of what every iteration exercises.
    /// </summary>
    private const int AnalyzedCeiling = 120;

    private static readonly DateTimeOffset ScannedAt = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly string Placeholder =
        SensitiveContentPlaceholder.For(MarkerSensitiveContentScanner.Category);

    private static readonly SensitiveContentPlan Plan = SensitiveContentPlan.Create(
        SensitiveContentScanBounds.Create(AnalyzedCeiling, TimeSpan.FromSeconds(15), maximumConcurrentScans: 2),
        [
            ScannerPlan(SensitiveContentScannerKind.Secrets),
            ScannerPlan(SensitiveContentScannerKind.Pii),
        ]);

    private static readonly Gen<string> DetectedValues = Gen.String[Gen.Char['0', '9'], 2, 6];

    /// <summary>
    /// A text built around two detected values: prose, digits that nearly match, and characters that pair. The second
    /// value is drawn either freely or as one starting inside the first, so that a fragment holding the first followed
    /// by the tail carries two findings that overlap.
    /// </summary>
    private static readonly Gen<(string First, string Second, string Text)> Texts =
        from first in DetectedValues
        from tail in Gen.String[Gen.Char['0', '9'], 1, 3]
        from second in Gen.OneOf(DetectedValues, Gen.Const(string.Concat(first.AsSpan(1), tail)))
        from fragments in Gen
            .OneOf(
                Gen.String[Gen.Char["abcdefghij ,.\n"], 0, 30],
                Gen.OneOfConst(first, second, string.Concat(first, tail), string.Concat(first, second)),
                Gen.String[Gen.Char['0', '9'], 1, 10],
                Gen.OneOfConst("😀", "👩‍👩‍👧‍👦", "🇵🇱"))
            .Array[0, 40]
        select (first, second, string.Concat(fragments));

    /// <summary>Whatever the text was, and wherever the values sat in it, neither is readable in the result.</summary>
    [Fact]
    public async Task RedactAsync_AnyTextHoldingDetectedValues_LeavesNoneOfThemInTheResult()
    {
        // Act, Assert
        await PropertyCheck.HoldsAsync(
            Texts,
            async input =>
            {
                var redacted = await RedactAsync(input);

                Assert.DoesNotContain(input.First, redacted.Text, StringComparison.Ordinal);
                Assert.DoesNotContain(input.Second, redacted.Text, StringComparison.Ordinal);
            },
            Iterations);
    }

    /// <summary>
    /// A redaction that hid a word beside the credential would be as wrong as one that hid nothing: a reader would meet
    /// a message with a hole in it and a citation would land on text nobody wrote. So what survives is exactly the
    /// characters no finding covered, and where two findings overlap the covering rule decides that rather than
    /// whichever detector answered first.
    /// </summary>
    [Fact]
    public async Task RedactAsync_AnyTextHoldingDetectedValues_KeepsExactlyTheCharactersNoFindingCovered()
    {
        // Act, Assert
        await PropertyCheck.HoldsAsync(
            Texts,
            async input =>
            {
                var redacted = await RedactAsync(input);

                var analyzed = input.Text[..(input.Text.Length - redacted.OmittedCharacterCount)];
                var covered = redacted.Findings
                    .SelectMany(finding => Enumerable.Range(finding.Span.Start, finding.Span.Length))
                    .ToHashSet();

                Assert.Equal(
                    string.Concat(analyzed.Where((_, at) => !covered.Contains(at))),
                    redacted.Text.Replace(Placeholder, string.Empty, StringComparison.Ordinal));
            },
            Iterations);
    }

    private static SensitiveContentScannerPlan ScannerPlan(SensitiveContentScannerKind scanner) =>
        SensitiveContentScannerPlan.Create(scanner, [MarkerSensitiveContentScanner.Category], []);

    private static async Task<RedactedText> RedactAsync((string First, string Second, string Text) input)
    {
        using var permits = new SensitiveContentScanConcurrency(Plan.Bounds.MaximumConcurrentScans);

        return await Redactor(input.First, input.Second, permits)
            .RedactAsync(input.Text, TestContext.Current.CancellationToken);
    }

    /// <summary>Builds the redactor of a deployment scanning for two literals, on a clock nothing here advances.</summary>
    /// <remarks>
    /// The clock is a fake for the reason every clock in this suite is, and here it also decides the outcome: the
    /// per-scan budget is timed on it, so a real one would let a loaded machine report a detector that answered as one
    /// that did not.
    /// </remarks>
    private static SensitiveContentRedactor Redactor(
        string first,
        string second,
        SensitiveContentScanConcurrency permits)
    {
        var timeProvider = new FakeTimeProvider(ScannedAt);

        return new SensitiveContentRedactor(
            Plan,
            [
                new MarkerSensitiveContentScanner(first, SensitiveContentScannerKind.Secrets, timeProvider),
                new MarkerSensitiveContentScanner(second, SensitiveContentScannerKind.Pii, timeProvider),
            ],
            timeProvider,
            permits);
    }
}
