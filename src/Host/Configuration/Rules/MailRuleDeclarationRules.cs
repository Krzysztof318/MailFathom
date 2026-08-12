// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Rules;
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
    /// <param name="declaredAccounts">The identifiers of the accounts the deployment declares, which a rule's scope may name.</param>
    /// <returns>One message per rule the declaration breaks, empty when it is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compiler" /> or <paramref name="declaredAccounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An absent section is a supported deployment rather than a failure: it applies no rules, which is what every
    /// deployment did before anybody wrote one.
    /// </remarks>
    public static IReadOnlyList<string> FindDeclarationErrors(
        MailRulesOptions? candidate,
        IMailRuleConditionCompiler compiler,
        IReadOnlyCollection<string> declaredAccounts)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(declaredAccounts);

        if (candidate is null)
        {
            return [];
        }

        var sectionErrors = FindSectionErrors(candidate).ToArray();
        var declarations = candidate.Rules
            .Select((rule, position) => (Rule: rule, Errors: FindDeclaredRuleErrors(rule, position, declaredAccounts)))
            .ToArray();
        var ruleErrors = declarations.SelectMany(declaration => declaration.Errors).ToArray();

        // The limits are what a condition is read under, so a section whose own limits are unusable has nothing to read
        // a condition against and stops here. A single badly declared rule stops nothing: every other rule's condition
        // is still read, because an operator fixing a rule set should see everything wrong with it in one reading.
        if (sectionErrors.Length > 0)
        {
            return [.. sectionErrors, .. ruleErrors];
        }

        var readable = declarations
            .Where(declaration => declaration.Errors.Count == 0)
            .Select(declaration => declaration.Rule);

        return [.. ruleErrors, .. FindConditionErrors(candidate, compiler, readable)];
    }

    /// <summary>Runs everything about one rule that can be judged without reading its condition.</summary>
    private static IReadOnlyList<string> FindDeclaredRuleErrors(
        MailRuleOptions rule,
        int position,
        IReadOnlyCollection<string> declaredAccounts) =>
    [
        .. FindRuleErrors(rule, position),
        .. FindScopeErrors(rule, position, declaredAccounts),
        .. FindIdentityErrors(rule, position),
    ];

    private static IEnumerable<string> FindConditionErrors(
        MailRulesOptions candidate,
        IMailRuleConditionCompiler compiler,
        IEnumerable<MailRuleOptions> readable)
    {
        var bounds = candidate.ToBounds();

        return readable
            .Where(rule => rule.Enabled)
            .Select(rule => compiler.Compile(rule.Name, rule.Condition, bounds))
            .SelectMany(compilation => compilation.Errors)
            .Select(DescribeRuleError);
    }

    /// <summary>Refuses anything a rule set's derived identity separates its own fields with.</summary>
    /// <remarks>
    /// The identity is a digest over the declared rules, and it can only tell two rule sets apart while no field can
    /// contain the character that ends a field. A rule name cannot, because its own pattern admits no control
    /// character; a condition and an account identifier are otherwise unrestricted text, so this is where the claim is
    /// made true rather than assumed.
    /// </remarks>
    private static IEnumerable<string> FindIdentityErrors(MailRuleOptions rule, int position)
    {
        var offending = new[] { nameof(MailRuleOptions.Condition) }
            .Where(_ => MailRuleSetRevision.ContainsSeparator(rule.Condition))
            .Concat(rule.Accounts
                .Where(MailRuleSetRevision.ContainsSeparator)
                .Select(_ => nameof(MailRuleOptions.Accounts)));

        return offending
            .Distinct(StringComparer.Ordinal)
            .Select(member =>
                $"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}:{position}:{member} — carries a separator character that a rule set's identity is derived with, so two different rule sets could be named the same revision.");
    }

    /// <summary>Judges one rule's account scope, which no attribute can reach because it is a claim about another section.</summary>
    /// <remarks>
    /// An unknown account is refused rather than ignored, for the reason the whole section is bound strictly: a rule
    /// scoped to an account nobody declared reaches no mail, and does so in silence. Ordinal comparison, because the
    /// synchronization section already tells two identifiers apart that way, so accepting a differently-cased spelling
    /// here would scope a rule to an account that is not the one the operator named.
    /// </remarks>
    private static IEnumerable<string> FindScopeErrors(
        MailRuleOptions rule,
        int position,
        IReadOnlyCollection<string> declaredAccounts)
    {
        var opening =
            $"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}:{position}:{nameof(MailRuleOptions.Accounts)}";
        var scope = rule.Accounts.Select(account => account?.Trim() ?? string.Empty).ToArray();

        if (scope.Any(string.IsNullOrEmpty))
        {
            yield return $"{opening} — an account this rule applies to is named by nothing.";

            yield break;
        }

        var repeated = scope
            .GroupBy(account => account, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (repeated is not null)
        {
            yield return $"{opening} — the account '{repeated.Key}' is named more than once.";
        }

        foreach (var unknown in scope.Where(account => !declaredAccounts.Contains(account, StringComparer.Ordinal)))
        {
            yield return
                $"{opening} — no account named '{unknown}' is declared under MailSynchronization:Accounts, so this rule would reach no mail.";
        }
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
