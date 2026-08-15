// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using System.Net.Http.Json;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>Finds personal data in text by asking an analyzer deployed beside this service.</summary>
/// <remarks>
/// <para>
/// Personal-data detection beyond the fixed-format identifiers needs a language model, and MailFathom loads none into its
/// own process, so this scanner is the one that reaches across a process boundary. It is still the same port: what runs
/// the detection and where is an adapter's business, and no analyzer type, HTTP type, or model type crosses the line
/// above.
/// </para>
/// <para>
/// <b>Every category goes through the analyzer, the fixed-format ones included.</b> A payment card number could be matched
/// here with a checksum and no model at all, and splitting the categories across two implementations would leave two
/// things deciding what a personal-data finding is: they would disagree about the same message, each would need a
/// false-positive corpus of its own, and the deployment rule — one analyzer, deployed only when the switch is on — would
/// become a rule per category.
/// </para>
/// <para>
/// The bounds are not this type's work. <see cref="Application.SensitiveContent.Redaction.SensitiveContentRedactor" />
/// caps the analyzed length, times
/// the per-call budget, and limits how many scans run at once before the text arrives, so a slow analyzer surfaces as this
/// deployment's own budget rather than as a transport failure from underneath it. What this adds is the request's own
/// shape: one client per call from the factory, and a failure of any kind reported as a scanner that could not establish
/// what the text carries rather than as a text that carried nothing.
/// </para>
/// <para>
/// <b>A configured language is a request, and they are made one after another.</b> One analyze call states one language, so
/// a deployment configured for two asks twice over the same text and merges what came back. Sequentially rather than at
/// once, for the reason the redactor runs its scanners in sequence: the concurrency bound counts scans rather than
/// requests, and fanning a caller's languages out would let one permit hold several analyzer calls open. What that costs
/// is real and belongs to the operator — every configured language shares the one per-scan budget rather than receiving a
/// budget of its own.
/// </para>
/// <para>
/// One instance serves the whole process and several scans at once. The requested entity list is fixed at construction and
/// nothing here holds state between calls.
/// </para>
/// <para>
/// Marked as verified by the integration suite even though the unit suite exercises every branch of it through a scripted
/// handler, because the claim this class exists to make is not one a scripted handler can settle: that a real analyzer, on
/// the image the deployment pulls, answers the request built here with the entities named here, in a shape mapped back
/// without dropping a finding and over offsets that land on the region of the original text. A handler answering with a
/// payload somebody hand-wrote proves the mapping works on the payload somebody hand-wrote.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PresidioContentScanner : ISensitiveContentScanner
{
    /// <summary>The analyzer route that answers what a text carries, resolved against the configured base address.</summary>
    private static readonly Uri AnalyzeRoute = new("analyze", UriKind.Relative);

    private readonly IHttpClientFactory transportFactory;
    private readonly PersonalDataAnalyzerProfile profile;
    private readonly TimeProvider timeProvider;
    private readonly FrozenDictionary<string, SensitiveContentRule> requestedRules;
    private readonly string[] requestedEntities;

    /// <summary>Initializes the scanner for the categories a deployment switched on.</summary>
    /// <param name="plan">What this deployment scans for, of which the personal-data half is read.</param>
    /// <param name="profile">Where the analyzer is, which languages it is asked in, and what its findings are attributed to.</param>
    /// <param name="transportFactory">Opens the client each call is made through.</param>
    /// <param name="timeProvider">Stamps each finding with when the scan evaluated the text.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the plan does not switch this scanner on.</exception>
    public PresidioContentScanner(
        SensitiveContentPlan plan,
        PersonalDataAnalyzerProfile profile,
        IHttpClientFactory transportFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.requestedRules = PresidioEntityCorpus.RequestedRules(plan);
        this.requestedEntities = [.. this.requestedRules.Keys.Order(StringComparer.Ordinal)];
        this.profile = profile;
        this.transportFactory = transportFactory;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner => SensitiveContentScannerKind.Pii;

    /// <inheritdoc />
    public SensitiveContentDetector Detector => this.profile.Detector;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SensitiveContentFinding>> ScanAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        cancellationToken.ThrowIfCancellationRequested();

        // The analyzer refuses a request carrying no text at all, which would reach the caller as a scanner that could
        // not answer and would fail an operation over an empty body. Nothing is in an empty text, so nothing is asked.
        if (text.Length == 0)
        {
            return [];
        }

        // One stamp for the whole scan rather than one per language, because what it records is when this scanner
        // evaluated the text, and the text was evaluated once however many questions that took.
        var detectedAt = this.timeProvider.GetUtcNow();
        var offsets = CodePointOffsets.For(text);
        var found = new List<SensitiveContentFinding>();

        foreach (var language in this.profile.Languages)
        {
            found.AddRange(this.Findings(offsets, await this.AnalyzeAsync(text, language, cancellationToken), detectedAt));
        }

        return Merged(found);
    }

    /// <summary>Collapses what two languages reported over the same value into the one detection it is.</summary>
    /// <remarks>
    /// A language-agnostic recognizer — an IBAN, an email address, a phone number — answers identically in every language
    /// the analyzer is asked in, so a deployment configured for two would otherwise report every such value twice. The
    /// redacted text would be unaffected, since overlapping regions merge into one placeholder, but the findings travel
    /// beyond that text: they are counted, recorded, and attributed, and a count that doubles when a language is added
    /// describes the configuration rather than the mail. The strongest score survives, which is the analyzer's own answer
    /// for the language that read the surrounding text best.
    /// </remarks>
    private static IReadOnlyList<SensitiveContentFinding> Merged(List<SensitiveContentFinding> found) =>
    [
        .. found
            .GroupBy(finding => (finding.Rule.Name, finding.Span.Start, finding.Span.End))
            .Select(duplicates => duplicates.MaxBy(finding => finding.Confidence)!),
    ];

    private async Task<PresidioRecognizedEntity[]> AnalyzeAsync(
        string text,
        string language,
        CancellationToken cancellationToken)
    {
        try
        {
            // Opened per call rather than held, so a client always carries the handler chain the factory considers
            // current: one kept in a field would go on resolving the analyzer's name to whatever it resolved to when
            // this singleton was constructed, which in a cluster is an address that has since moved.
            using var transport = this.transportFactory.CreateClient(PersonalDataAnalyzerProfile.TransportName);
            using var request = JsonContent.Create(
                new PresidioAnalyzeRequest(
                    text,
                    language,
                    this.requestedEntities,
                    this.profile.MinimumConfidence),
                PresidioJsonContext.Default.PresidioAnalyzeRequest);
            using var response = await transport.PostAsync(AnalyzeRoute, request, cancellationToken);

            // The body of a refusal is deliberately not read. It is composed by a service this process does not own and
            // the analyzer quotes the failure it met, which on a malformed request is the request itself.
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync(
                    PresidioJsonContext.Default.PresidioRecognizedEntityArray,
                    cancellationToken)
                ?? throw new InvalidOperationException("The analyzer answered with a JSON null rather than a list.");
        }
        catch (OperationCanceledException)
        {
            // The caller's budget or the host's shutdown. Both are facts about this process rather than about the
            // analyzer, and the redactor above tells them apart.
            throw;
        }
        catch (Exception failure)
        {
            throw SensitiveContentScannerUnavailableException.Failed(SensitiveContentScannerKind.Pii, failure);
        }
    }

    /// <summary>Maps what the analyzer reported onto findings, dropping what this deployment did not ask about.</summary>
    /// <remarks>
    /// An entity the mapping does not know is ignored rather than refused. An analyzer may run recognizers of its own, and
    /// one reporting something no category covers is answering a question nobody asked rather than failing — while a
    /// deployment that refused the whole scan over it would fail closed on every message.
    /// </remarks>
    private IReadOnlyList<SensitiveContentFinding> Findings(
        CodePointOffsets offsets,
        PresidioRecognizedEntity[] reported,
        DateTimeOffset detectedAt) =>
    [
        .. reported
            .Where(entity => entity.EntityType is not null
                && this.requestedRules.ContainsKey(entity.EntityType))
            .Select(entity => this.Finding(offsets, entity, detectedAt)),
    ];

    private SensitiveContentFinding Finding(
        CodePointOffsets offsets,
        PresidioRecognizedEntity entity,
        DateTimeOffset detectedAt)
    {
        if (!offsets.TryTranslate(entity.Start, entity.End, out var span))
        {
            // A region that is not a region of the text this process just sent is a fault rather than a detection, and
            // the port says a scanner that could not establish what the text carries refuses the operation it guards.
            throw SensitiveContentScannerUnavailableException.Failed(
                SensitiveContentScannerKind.Pii,
                new InvalidOperationException(
                    "The analyzer reported an entity outside the text it was handed, so what it found could not be located."));
        }

        return SensitiveContentFinding.Create(
            this.requestedRules[entity.EntityType!],
            span,
            Math.Clamp(entity.Score, 0, 1),
            this.profile.Detector,
            detectedAt);
    }
}
