// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Application.SensitiveContent;
using Microsoft.Security.Utilities;

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>One entry of the secret corpus: the rule an operator can name, and the expression that finds it.</summary>
/// <remarks>
/// <para>
/// The corpus is assembled from two places, and this type is what makes that invisible above it. An entry
/// <see cref="Compile" /> produced carries an expression MailFathom compiled into this assembly; one
/// <see cref="Adopt" /> produced carries a pattern the detection engine already ships. Both run the same way, and both
/// report a finding under one detector identity and one corpus revision, so an operator diagnosing a false positive
/// never has to know which of the two supplied the rule.
/// </para>
/// <para>
/// The compiled expression is kept beside the pattern because a pattern declares itself as a string.
/// <see cref="SecretRegexEngine" /> maps the string back to the matcher this entry was built from, which is what lets a
/// source-generated matcher be reached from a declaration that never names one.
/// </para>
/// </remarks>
internal sealed record SecretRuleDefinition
{
    /// <summary>The group an expression narrows its finding to, where the whole match is more than the credential.</summary>
    /// <remarks>
    /// The name is the detection engine's own convention, which is why an expression written here uses it too. An
    /// expression that declares no such group reports its whole match, which is what a rule whose match <em>is</em> the
    /// credential wants.
    /// </remarks>
    public const string SecretCaptureGroup = "refine";

    private const double CertainConfidence = 1;

    private SecretRuleDefinition(SensitiveContentRule rule, RegexPattern pattern, Regex? expression, double confidence)
    {
        this.Rule = rule;
        this.Pattern = pattern;
        this.Expression = expression;
        this.Confidence = confidence;
    }

    /// <summary>Gets the rule this entry declares, which is the name a suppression reaches it by.</summary>
    public SensitiveContentRule Rule { get; }

    /// <summary>Gets the pattern as the detection engine registers it.</summary>
    public RegexPattern Pattern { get; }

    /// <summary>Gets the matcher MailFathom compiled for this entry, or <see langword="null" /> when the engine ships its own.</summary>
    public Regex? Expression { get; }

    /// <summary>Gets how sure a match under this rule is, from 0 to 1 inclusive.</summary>
    /// <remarks>
    /// A rule that recognises a credential by its own format reports 1: the format is the evidence. Only the entropy
    /// heuristic scores a candidate, and it computes its confidence per match rather than reading it from here.
    /// </remarks>
    public double Confidence { get; }

    /// <summary>Declares a rule whose expression MailFathom compiled into this assembly.</summary>
    /// <param name="category">The category the rule belongs to.</param>
    /// <param name="name">The rule's name, as the corpus it came from spells it.</param>
    /// <param name="expression">The source-generated matcher.</param>
    /// <returns>The corpus entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SecretRuleDefinition Compile(
        SensitiveContentCategory category,
        string name,
        Regex expression)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(expression);

        var rule = SensitiveContentRule.Create(category, name);

        // No signature literals. The engine offers them as a prefilter, and every way of deriving one from an
        // expression that carries an alternation names a literal some matches do not contain — which switches the rule
        // off for exactly those matches, silently. Measured over this corpus they also cost more than they save: a
        // whole-input scan per literal is more work than the matchers it skips.
        var pattern = new RegexPattern(
            id: rule.Name,
            name: rule.Name,
            label: rule.Name,
            patternMetadata: DetectionMetadata.HighConfidence,
            pattern: expression.ToString(),
            regexOptions: expression.Options);

        return new SecretRuleDefinition(rule, pattern, expression, CertainConfidence);
    }

    /// <summary>Declares a rule from a pattern the detection engine already ships.</summary>
    /// <param name="category">The category the rule belongs to.</param>
    /// <param name="pattern">The engine's own pattern, whose name becomes the rule's.</param>
    /// <param name="confidence">How sure a match under this rule is.</param>
    /// <returns>The corpus entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SecretRuleDefinition Adopt(
        SensitiveContentCategory category,
        RegexPattern pattern,
        double confidence = CertainConfidence)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(pattern);

        return new SecretRuleDefinition(
            SensitiveContentRule.Create(category, pattern.Name),
            pattern,
            expression: null,
            confidence);
    }
}
