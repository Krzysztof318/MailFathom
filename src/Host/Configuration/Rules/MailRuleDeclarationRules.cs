// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Holds every rule a declared rule set must satisfy, so composition and a reload judge one by the same reading.</summary>
/// <remarks>
/// <para>
/// The section is read twice in the life of a process: once while the host composes itself, and again for every
/// candidate a configuration reload produces. One set of rules answers both, because a second copy would drift into a
/// rule set startup accepted and a reload refused, or the reverse — and the reverse is the worse of the two, because it
/// would put rules nothing proved in front of somebody's mail.
/// </para>
/// <para>
/// The attribute bounds are run here rather than left to <c>ValidateDataAnnotations</c> for two reasons. The options
/// framework raises a rejected reload on the thread that reported the configuration change, where the failure has
/// nowhere to be reported and the candidate is dropped without a word. And its validation does not descend into the
/// elements of a collection, so a rule with no name or no condition would otherwise pass every check until something
/// tried to use it.
/// </para>
/// <para>
/// Compiling every condition is the expensive half and it belongs here, because compiling is what checks a condition
/// against the fact surface. That is the whole point of reading a rule set before any mail is seen: an unknown fact or
/// a comparison that could never hold is refused at startup rather than raised over real correspondence.
/// </para>
/// </remarks>
internal static class MailRuleDeclarationRules
{
    /// <summary>Reports everything an operator must fix before a declared rule set can be used.</summary>
    /// <param name="candidate">The bound declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="compiler">Reads each condition against the fact surface.</param>
    /// <returns>One message per rule the declaration breaks, empty when it is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compiler" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An absent section is a supported deployment rather than a failure: it applies no rules, which is what every
    /// deployment did before anybody wrote one.
    /// </remarks>
    public static IReadOnlyList<string> FindDeclarationErrors(
        MailRulesOptions? candidate,
        IMailRuleConditionCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);

        if (candidate is null)
        {
            return [];
        }

        var errors = new List<string>(FindSectionErrors(candidate));

        errors.AddRange(candidate.Rules.SelectMany((rule, position) => FindRuleErrors(rule, position)));

        // The limits are what a condition is read under, so a rule set whose limits are themselves unusable has nothing
        // to read the conditions against and the messages above are the ones worth reporting.
        return errors.Count > 0
            ? errors
            : [.. FindConditionErrors(candidate, compiler)];
    }

    private static IEnumerable<string> FindConditionErrors(
        MailRulesOptions candidate,
        IMailRuleConditionCompiler compiler)
    {
        var bounds = candidate.ToBounds();

        return candidate.Rules
            .Where(rule => rule.Enabled)
            .Select(rule => compiler.Compile(rule.Name, rule.Condition, bounds))
            .SelectMany(compilation => compilation.Errors)
            .Select(DescribeRuleError);
    }

    /// <summary>Runs the section's own attribute bounds and everything <see cref="MailRulesOptions.Validate" /> reports.</summary>
    private static IEnumerable<string> FindSectionErrors(MailRulesOptions candidate) =>
        Validate(candidate).Select(result => $"{DescribeConfigurationPath(result)} — {result.ErrorMessage}");

    /// <summary>Runs one rule's attribute bounds, naming the position it sits at because a nameless rule has nothing else.</summary>
    private static IEnumerable<string> FindRuleErrors(MailRuleOptions rule, int position) =>
        Validate(rule).Select(result =>
            $"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}:{position}:{result.MemberNames.FirstOrDefault() ?? string.Empty} — {result.ErrorMessage}");

    private static IEnumerable<ValidationResult> Validate(object candidate)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            candidate,
            new ValidationContext(candidate),
            results,
            validateAllProperties: true);

        return results.Where(result => !string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    /// <summary>Names the configuration key a condition's refusal is about; the message itself already names the rule.</summary>
    private static string DescribeRuleError(string error) =>
        $"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)} — {error}";

    /// <summary>Names the configuration key a validation result is about, falling back to the section when it names no member.</summary>
    private static string DescribeConfigurationPath(ValidationResult result) =>
        result.MemberNames.FirstOrDefault() is { Length: > 0 } memberName
            ? $"{MailRulesOptions.SectionName}:{memberName}"
            : MailRulesOptions.SectionName;
}
