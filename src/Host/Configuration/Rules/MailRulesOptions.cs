// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Declares the rules this deployment applies to its mail, and the limits every one of them is read under.</summary>
/// <remarks>
/// <para>
/// A configuration section rather than a table, which is the decision
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0010-rule-authoring-in-configuration-and-ncalc-conditions.md">ADR 0010</see>
/// records: what an instance will do to a mailbox is the most consequential thing it declares, and configuration is
/// what makes that reviewable in a diff before it runs and reproducible from a repository afterwards. Nothing creates,
/// edits, or deletes a rule at run time.
/// </para>
/// <para>
/// The whole section is optional and every limit has a usable default, so an absent section is a deployment that
/// applies no rules rather than one that applies unbounded ones.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRulesOptions : IValidatableObject
{
    /// <summary>The configuration section this declaration is bound from.</summary>
    public const string SectionName = "MailRules";

    /// <summary>Gets or sets the rules, in the order they are evaluated in.</summary>
    /// <remarks>
    /// The count is capped because a pass evaluates every rule it reaches for every email, so the number of rules is
    /// the one multiplier on that cost that a rule set declares rather than inherits.
    /// </remarks>
    [MaxLength(200)]
    public IList<MailRuleOptions> Rules { get; set; } = [];

    /// <summary>Gets or sets the greatest number of characters one condition may be written in.</summary>
    [Range(1, 10_000)]
    public int MaxConditionLength { get; set; } = 1_000;

    /// <summary>Gets or sets the greatest depth one condition may nest to.</summary>
    [Range(1, 64)]
    public int MaxConditionNestingDepth { get; set; } = 16;

    /// <summary>The longest a condition may be given, which is the ceiling the two written limits have and this one needs.</summary>
    /// <remarks>
    /// A timeout is the only bound on a condition that nothing about the text can supply, so an operator who meant
    /// milliseconds and wrote minutes would otherwise let one condition hold a pass for as long as it liked, per email.
    /// Thirty seconds is far above anything a metadata comparison or a single stored-content read needs and far below
    /// the point at which a stuck condition stops being visible as one.
    /// </remarks>
    public static readonly TimeSpan LongestConditionEvaluationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long one condition may take to evaluate, including resolving the facts it names.</summary>
    public TimeSpan ConditionEvaluationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets how many stored emails one evaluation batch reads, evaluates, and commits together.</summary>
    /// <remarks>
    /// A batch is the unit of progress an interrupted pass gives back, and the unit of work one transaction covers.
    /// Nothing about it is a schedule: when a pass happens is the account's synchronization interval.
    /// </remarks>
    [Range(1, 10_000)]
    public int EvaluationBatchSize { get; set; } = 200;

    /// <summary>Gets or sets how many batches one walk of one pass may commit before leaving the rest to the next run.</summary>
    /// <remarks>
    /// What bounds a run rather than what bounds the work: an account whose mail has never been evaluated drains over
    /// as many runs as its size needs, instead of turning one run into a walk of its whole history while the folders
    /// that run exists to fetch wait behind it.
    /// </remarks>
    [Range(1, 1_000)]
    public int MaxEvaluationBatchesPerPass { get; set; } = 5;

    /// <summary>Turns the declared limits into the bounds a condition is read and run under.</summary>
    /// <returns>The bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit is not positive, which validation refuses first.</exception>
    public MailRuleConditionBounds ToBounds() => MailRuleConditionBounds.Create(
        this.MaxConditionLength,
        this.MaxConditionNestingDepth,
        this.ConditionEvaluationTimeout);

    /// <summary>Turns the declared limits into the bounds one evaluation pass runs under.</summary>
    /// <returns>The bounds.</returns>
    public MailRuleEvaluationOptions ToEvaluationOptions() => new()
    {
        BatchSize = this.EvaluationBatchSize,
        MaxBatchesPerPass = this.MaxEvaluationBatchesPerPass,
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.ConditionEvaluationTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{SectionName} declares a ConditionEvaluationTimeout that is not positive, so every condition would run out of time before it started and every rule would be recorded as failed.",
                [nameof(this.ConditionEvaluationTimeout)]);
        }
        else if (this.ConditionEvaluationTimeout > LongestConditionEvaluationTimeout)
        {
            yield return new ValidationResult(
                $"{SectionName} declares a ConditionEvaluationTimeout of {this.ConditionEvaluationTimeout}, and a condition may be given at most {LongestConditionEvaluationTimeout}. A timeout above that stops bounding what one rule costs per email.",
                [nameof(this.ConditionEvaluationTimeout)]);
        }

        var duplicateName = this.Rules
            .Select(rule => rule.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateName is not null)
        {
            yield return new ValidationResult(
                $"{SectionName} declares more than one rule named '{duplicateName.Key}'. A rule is reported by its name, so two rules answering to one name could not be told apart in a log.",
                [nameof(this.Rules)]);
        }
    }
}
