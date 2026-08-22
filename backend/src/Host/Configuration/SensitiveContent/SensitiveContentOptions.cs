// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.SensitiveContent;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Configures what this deployment scans mail for before that mail is copied or handed out.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own, because what a deployment scans for is a property of the deployment rather than of
/// its database, its mail accounts, or its AI providers, and because both switches below reach several of those at once.
/// </para>
/// <para>
/// Both scanners are off by default, and an absent section is that default rather than a startup failure. What the
/// section holds beside them is what one scan may spend, which every deployment has whether or not it scans anything.
/// </para>
/// <para>
/// The names configured under each switch are validated against what the registered scanners declare, which is a
/// separate pass rather than an attribute here: the answer depends on which scanners this build carries, so it is
/// reached through <see cref="SensitiveContentDeclarationRules" /> at startup instead of from a rule on a property.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SensitiveContentOptions : IValidatableObject
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "SensitiveContent";

    /// <summary>Gets the scanner that looks for credentials, tokens, and keys, and runs inside this process.</summary>
    public SensitiveContentScannerOptions Secrets { get; } = new();

    /// <summary>Gets the scanner that looks for personal data, and reaches an analyzer deployed beside this process.</summary>
    public SensitiveContentScannerOptions Pii { get; } = new();

    /// <summary>Gets where that analyzer is, what language it is asked in, and how sure it must be.</summary>
    public PersonalDataAnalyzerOptions PersonalDataAnalyzer { get; } = new();

    /// <summary>Gets or sets which scanners stop an outgoing message rather than being read for a placeholder.</summary>
    /// <remarks>
    /// <para>
    /// The switches above decide what this deployment <em>detects</em>, and it detects the same things everywhere. This
    /// decides what a detection <em>does</em> on the one path where redaction is not an answer: a message being queued
    /// for transmission or filed as a draft is somebody's own text, so a finding refuses the act instead of being
    /// replaced in it. Naming a scanner here is what turns its findings into refusals; a scanner not named goes on
    /// guarding every other path exactly as before.
    /// </para>
    /// <para>
    /// <b>The default is the secrets scanner alone, and the omission of the other one is the point.</b> A credential in
    /// a message somebody is sending was never meant to be in it, while the names, addresses, and signature blocks the
    /// personal-data scanner reports are most of what ordinary correspondence is made of — a deployment that let those
    /// stop a send would have turned sending off by a longer route. An operator handling regulated correspondence names
    /// both and accepts that.
    /// </para>
    /// <para>
    /// A scanner named here while its own switch is off screens nothing, and that is not refused: the switch is what
    /// decides whether a scanner runs at all, and a deployment that turned one off has answered for every path it would
    /// have reached. Writing an empty list screens nothing at all, which is how a deployment keeps redaction on every
    /// other path and lets its own mail leave unscreened.
    /// </para>
    /// <para>
    /// It is an array rather than the <c>IList</c> its neighbours use, because <c>[]</c> has to be distinguishable from
    /// an absent key here and only an array property tells the two apart: the binder leaves a list untouched at its
    /// default, so an operator writing an empty list would silently get the default instead of the nothing they asked
    /// for. The entries stay <c>string</c> and are parsed rather than bound as the enum, because a bound element that
    /// fails to convert is dropped and a one-entry list whose only entry was mistyped would arrive as the empty list,
    /// which now means something.
    /// </para>
    /// </remarks>
    public string[]? ScreenOutgoingMailFor { get; set; }

    /// <summary>Gets or sets the greatest number of characters one scan analyzes.</summary>
    /// <remarks>
    /// Text beyond it is dropped from the result rather than handed on unscanned. The default matches what a single
    /// content read may return in total, so an ordinary mail body is analyzed whole.
    /// </remarks>
    [Range(1, 10_000_000)]
    public int MaximumAnalyzedCharacters { get; set; } = SensitiveContentScanBounds.Default.MaximumAnalyzedCharacters;

    /// <summary>Gets or sets how long one call to one scanner may take before the operation it guards is refused.</summary>
    public TimeSpan ScanTimeout { get; set; } = SensitiveContentScanBounds.Default.ScanTimeout;

    /// <summary>Gets or sets how many scans may run at once across the process.</summary>
    [Range(1, 256)]
    public int MaximumConcurrentScans { get; set; } = SensitiveContentScanBounds.Default.MaximumConcurrentScans;

    /// <summary>Gets or sets whether the extraction backfill re-derives what was written under an older configuration.</summary>
    /// <remarks>
    /// <para>
    /// Off by default, and deliberately not implied by switching a scanner on. A scanner protects what is derived from
    /// now on; the chunks and vectors of mail that was indexed earlier were built from unredacted text and stay that way
    /// until somebody re-derives them, which costs a pass over every stored message's raw MIME and, where an embedding
    /// profile is active, a re-embedding of every passage whose text changed. Spending that over a whole mailbox is the
    /// operator's decision rather than a consequence of editing a category list.
    /// </para>
    /// <para>
    /// It is here rather than beside the walk that performs it because it answers a question about this section: an
    /// operator who has just switched a scanner on is reading these keys, and what they need to know next is that the
    /// mail already stored is not covered. The rebuild rides the extraction backfill, so a deployment that switched
    /// <c>MailExtractionBackfill:Enabled</c> off performs none.
    /// </para>
    /// </remarks>
    public bool RebuildStaleDerivedData { get; set; }

    /// <summary>Gets whether this deployment scans anything at all.</summary>
    /// <remarks>
    /// This is what the composition root registers on. With both switches off no plan is composed, no redactor exists,
    /// and no detector is constructed, so an opt-in nobody took costs nothing on any path.
    /// </remarks>
    public bool IsAnyScannerEnabled => this.Secrets.Enabled || this.Pii.Enabled;

    /// <summary>Finds the scanner options one switch is configured by.</summary>
    /// <param name="scanner">The switch to read.</param>
    /// <returns>The options under that switch.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value names no switch.</exception>
    public SensitiveContentScannerOptions For(SensitiveContentScannerKind scanner) => scanner switch
    {
        SensitiveContentScannerKind.Secrets => this.Secrets,
        SensitiveContentScannerKind.Pii => this.Pii,
        _ => throw new ArgumentOutOfRangeException(nameof(scanner), scanner, "The value names no configured scanner."),
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A range attribute cannot express a TimeSpan bound, and an unbounded one is worth refusing in both directions:
        // a budget below a second refuses ordinary mail on a busy machine, and one measured in minutes is a stall
        // rather than a timeout.
        if (this.ScanTimeout < TimeSpan.FromSeconds(1) || this.ScanTimeout > TimeSpan.FromMinutes(2))
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:ScanTimeout is {1}, which is outside the range one scan may take: at least one second and at most two minutes.",
                    SectionName,
                    this.ScanTimeout),
                [nameof(this.ScanTimeout)]);
        }

        foreach (var result in this.FindScreeningErrors())
        {
            yield return result;
        }

        foreach (var result in this.FindAnalyzerErrors())
        {
            yield return result;
        }
    }

    /// <summary>Finds what is wrong with the scanners named as stopping an outgoing message.</summary>
    /// <remarks>
    /// The spelling is all that is judged. A name that is not one of the two scanners would otherwise be dropped in
    /// silence and read as a deployment that screens less than its own file says it does, which is the failure mode
    /// every list in this section is validated against. A correctly spelled scanner whose switch is off is not judged,
    /// for the reason stated where the key is declared.
    /// </remarks>
    private IEnumerable<ValidationResult> FindScreeningErrors()
    {
        if (this.ScreenOutgoingMailFor is not { } named)
        {
            yield break;
        }

        var accepted = Enum.GetNames<SensitiveContentScannerKind>();

        var unknown = named
            .Where(scanner => !accepted.Contains(scanner, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unknown.Length > 0)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(this.ScreenOutgoingMailFor)} names '{string.Join("', '", unknown)}', and every entry is one of the scanners this deployment can switch on: {string.Join(", ", accepted)}.",
                [nameof(this.ScreenOutgoingMailFor)]);
        }
    }

    /// <summary>Reports whether one configured entry is a code the analyzer can be asked in.</summary>
    /// <remarks>
    /// A null is one of the shapes a configuration source can bind into a list, and it has to be answered here rather
    /// than reached by the deduplication below, which refuses to hash one.
    /// </remarks>
    private static bool IsLanguageCode(string language) =>
        language is { Length: 2 } && language.All(char.IsAsciiLetterLower);

    /// <summary>Finds what is wrong with the analyzer block, which only a switched-on personal-data scanner reads.</summary>
    /// <remarks>
    /// Nothing here judges an analyzer address left behind under a scanner nobody runs, for the reason nothing judges a
    /// category list in that state: it describes no protection, so refusing to start over it would be refusing over a
    /// comment. What is refused is the reverse — the scanner on with nowhere to ask — because that deployment would fail
    /// every operation the scanner guards while its own configuration read as protection in force.
    /// </remarks>
    private IEnumerable<ValidationResult> FindAnalyzerErrors()
    {
        var analyzerKey = $"{SectionName}:{nameof(this.PersonalDataAnalyzer)}";

        if (!this.Pii.Enabled)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(this.PersonalDataAnalyzer.Endpoint))
        {
            yield return new ValidationResult(
                $"{SectionName}:Pii is switched on and {analyzerKey}:Endpoint names no address. The personal-data scanner reaches an analyzer deployed beside this service and fails closed without one, so state its address — http://presidio-analyzer:3000 in the deployments this repository ships — or switch the scanner off.",
                [nameof(this.PersonalDataAnalyzer)]);

            yield break;
        }

        // The value it was given is deliberately not echoed. A missing scheme is the commonest way to reach this branch, so
        // what would be quoted is the analyzer's own host name, and this message goes to a startup log like any other.
        if (!Uri.TryCreate(this.PersonalDataAnalyzer.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult(
                $"{analyzerKey}:Endpoint is not an absolute http or https address, so no request could be composed from it. State one such as http://presidio-analyzer:3000, or switch the scanner off.",
                [nameof(this.PersonalDataAnalyzer)]);
        }

        // Two letters each, because the analyzer selects a model by one and each code reaches a query string and the
        // detector revision every finding carries. A wider grammar would let a configured value decide how either one
        // parses. An empty list is not judged here: it is the absent list every collection in this section defaults from,
        // and SensitiveContentPlanMapper is what turns it into the shipped analyzer's own language.
        var malformed = this.PersonalDataAnalyzer.Languages.Where(language => !IsLanguageCode(language)).ToArray();

        if (malformed.Length > 0)
        {
            // Only the entries that failed, because quoting the whole list beside them leaves an operator comparing a
            // valid code with an invalid one and nothing saying which is which.
            yield return new ValidationResult(
                $"{analyzerKey}:Languages names '{string.Join("', '", malformed)}', and every entry is a two-letter lowercase language code such as en. State ones the analyzer's own configuration loads a model for.",
                [nameof(this.PersonalDataAnalyzer)]);
        }

        // Bounded because each language is another analyzer request inside the one budget ScanTimeout allows, and because
        // the revision every finding carries names them all inside a grammar of its own. Refused here so the message names
        // the key an operator wrote rather than the grammar a detector identity accepts. Counted after deduplication, as
        // PersonalDataAnalyzerProfile counts it: a repeat is one language asked once, so refusing over one would refuse a
        // configuration the profile accepts.
        var asked = this.PersonalDataAnalyzer.Languages
            .Where(IsLanguageCode)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (asked > PersonalDataAnalyzerProfile.MaximumLanguages)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:Languages names {1} distinct languages and at most {2} are asked for. One scan asks once per language inside the single budget {3}:ScanTimeout allows, and the derivation stamp names every one of them.",
                    analyzerKey,
                    asked,
                    PersonalDataAnalyzerProfile.MaximumLanguages,
                    SectionName),
                [nameof(this.PersonalDataAnalyzer)]);
        }

        // Checked here rather than through a range attribute, because ValidateDataAnnotations reads the properties of the
        // bound root and never descends into a block like this one, so an attribute would read as a bound and enforce
        // nothing.
        if (double.IsNaN(this.PersonalDataAnalyzer.MinimumConfidence)
            || this.PersonalDataAnalyzer.MinimumConfidence is < 0 or > 1)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:MinimumConfidence is {1}, which is not a share of certainty between 0 and 1. Redaction acts on every finding the analyzer reports, so this is what keeps its weakest guesses — a payment card also read as a bank account at 0.05 — out of the text.",
                    analyzerKey,
                    this.PersonalDataAnalyzer.MinimumConfidence),
                [nameof(this.PersonalDataAnalyzer)]);
        }
    }
}
