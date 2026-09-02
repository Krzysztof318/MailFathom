// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using System.Text.RegularExpressions;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>Finds credentials in text, in this process, by matching it against the secret corpus.</summary>
/// <remarks>
/// <para>
/// Secret detection is pattern matching over text, so it runs here rather than behind a process boundary: a container
/// of its own would buy nothing and would put a network hop and a failure mode on a read path that has to fail closed.
/// </para>
/// <para>
/// The scanner is built once, for the plan a deployment resolved. Only the categories that plan names are registered
/// with the engine, and a suppressed rule is left out of the registration rather than filtered afterwards, so a rule an
/// operator switched off costs nothing to run.
/// </para>
/// <para>
/// The work is processor-bound and completes on the calling thread, so the pass over the corpus is written here rather
/// than delegated to the engine's own masker: that one materializes every detection before it returns a single one, and
/// a caller's token observed afterwards would bound nothing. Running the patterns in this type puts the check between
/// them, so a cancelled caller or an overrun scan budget ends the pass within one expression instead of after all of
/// them. Three bounds then sit inside each other — the caller's analyzed ceiling and per-scan budget, the check between
/// patterns here, and the per-expression timeout <see cref="SecretRegexEngine" /> carries.
/// </para>
/// <para>
/// One instance serves the whole process and several scans at once. The corpus is fixed at construction and a compiled
/// matcher is thread-safe, so concurrent scans share the matchers and share no state at all.
/// </para>
/// </remarks>
internal sealed class SecretContentScanner : ISensitiveContentScanner
{
    /// <summary>How random a run must be, per character, before the entropy heuristic reports it.</summary>
    /// <remarks>
    /// Below this a run repeats or carries structure, which is what an encoded fragment of text, a message identifier,
    /// and a formatted reference all do. A credential drawn from a random alphabet sits well above it: a base64 secret
    /// approaches six bits per character and a hexadecimal one approaches four.
    /// </remarks>
    private const double EntropyFloorBitsPerCharacter = 3.5;

    /// <summary>The widest alphabet the corpus's entropy rules match over, which normalizes a measurement to a confidence.</summary>
    private const double EntropyCeilingBitsPerCharacter = 6;

    private readonly SecretRuleDefinition[] registered;
    private readonly SecretRegexEngine engine;
    private readonly FrozenDictionary<string, SecretRuleDefinition> active;
    private readonly SensitiveContentRule? unnamed;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the scanner for the categories a deployment switched on.</summary>
    /// <param name="plan">What this deployment scans for, of which the secrets half is read.</param>
    /// <param name="timeProvider">Stamps each finding with when the scan evaluated the text.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the plan does not switch this scanner on.</exception>
    public SecretContentScanner(SensitiveContentPlan plan, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!plan.TryGetScanner(SensitiveContentScannerKind.Secrets, out var secrets))
        {
            throw new ArgumentException(
                "The secret scanner was constructed for a deployment whose plan does not switch it on.",
                nameof(plan));
        }

        this.registered = [.. SecretRuleCorpus.Rules
            .Where(definition => secrets.Categories.Contains(definition.Rule.Category))
            .Where(definition => !secrets.Suppresses(definition.Rule))];

        this.active = this.registered.ToFrozenDictionary(
            definition => definition.Rule.Name,
            StringComparer.OrdinalIgnoreCase);

        this.unnamed = secrets.Categories.Contains(SecretRuleCorpus.UnnamedProviderCredential.Category)
            && !secrets.Suppresses(SecretRuleCorpus.UnnamedProviderCredential)
                ? SecretRuleCorpus.UnnamedProviderCredential
                : null;

        this.engine = new SecretRegexEngine(this.registered);
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner => SensitiveContentScannerKind.Secrets;

    /// <inheritdoc />
    public SensitiveContentDetector Detector => SecretRuleCorpus.Detector;

    /// <inheritdoc />
    public Task<IReadOnlyList<SensitiveContentFinding>> ScanAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult<IReadOnlyList<SensitiveContentFinding>>(this.Detect(text, cancellationToken));
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Anything the engine raises — an expression that met its timeout above all — is a scanner that could not
            // establish what the text carries, which the port says is refused rather than reported as nothing found.
            throw SensitiveContentScannerUnavailableException.Failed(SensitiveContentScannerKind.Secrets, failure);
        }
    }

    private List<SensitiveContentFinding> Detect(string text, CancellationToken cancellationToken)
    {
        var detectedAt = this.timeProvider.GetUtcNow();
        var findings = new List<SensitiveContentFinding>();

        // A loop rather than a pipeline: the token has to be observed between one expression and the next, which is
        // the only place a running scan can be ended at all.
        foreach (var definition in this.registered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var match in this.engine.Matches(
                text,
                definition.Pattern.Pattern,
                definition.Pattern.RegexOptions,
                captureGroup: SecretRuleDefinition.SecretCaptureGroup))
            {
                var finding = this.Finding(text, definition, match, detectedAt);

                if (finding is not null)
                {
                    findings.Add(finding);
                }
            }
        }

        return findings;
    }

    private SensitiveContentFinding? Finding(
        string text,
        SecretRuleDefinition definition,
        Group match,
        DateTimeOffset detectedAt)
    {
        if (!match.Success || match.Length <= 0)
        {
            return null;
        }

        // A pattern reads the match back to say what it found: one expression recognises a family of keys and the
        // provider signature inside the match names the product that issued it. A pattern that recognises nothing in
        // its own match rejects it, which is a match that was never a credential.
        var moniker = definition.Pattern.GetMatchIdAndName(match.Value);

        if (moniker is null)
        {
            return null;
        }

        var (rule, confidence) = this.Resolve(moniker.Item2);

        if (rule is null)
        {
            return null;
        }

        if (rule.Category == SecretCategories.HighEntropyString)
        {
            var bits = ShannonEntropy.BitsPerCharacter(text.AsSpan(match.Index, match.Length));

            if (bits < EntropyFloorBitsPerCharacter)
            {
                return null;
            }

            confidence = Math.Min(1, bits / EntropyCeilingBitsPerCharacter);
        }

        return SensitiveContentFinding.Create(
            rule,
            SensitiveContentSpan.Create(match.Index, match.Length),
            confidence,
            SecretRuleCorpus.Detector,
            detectedAt);
    }

    /// <summary>Finds the rule a detection belongs to, and how sure a match under it is.</summary>
    /// <param name="detectionName">The name the engine reported the match under.</param>
    /// <returns>The rule to report under and its confidence, or no rule where the finding is to be dropped.</returns>
    /// <remarks>
    /// <para>
    /// The engine refines the name of a match: one expression recognises a family of keys and then reads the provider
    /// signature inside the match to say which product issued it. So the name that arrives here is not always the name
    /// of the pattern that was registered, and the three answers below are the three cases that produces.
    /// </para>
    /// <para>
    /// A name the corpus declares but this deployment did not register is a rule an operator suppressed or a category
    /// they left out, and dropping the finding is what honours that decision. A name the corpus does not declare at all
    /// is the opposite case: it is reported under the rule kept for exactly that, rather than lost.
    /// </para>
    /// </remarks>
    internal (SensitiveContentRule? Rule, double Confidence) Resolve(string? detectionName)
    {
        if (detectionName is not null && this.active.TryGetValue(detectionName, out var registered))
        {
            return (registered.Rule, registered.Confidence);
        }

        if (detectionName is not null && SecretRuleCorpus.RulesByName.ContainsKey(detectionName))
        {
            return (null, 0);
        }

        return (this.unnamed, 1);
    }
}
