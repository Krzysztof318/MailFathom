// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Turns a declared rule set into the immutable set a pass runs against, under the identity it derives.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and carries rules that are switched off, while a rule set is the proven value a pass is
/// allowed to assume. The revision is derived from the declarations that survive that filter, so switching a rule off
/// moves the identity exactly as deleting it would.
/// </remarks>
internal static class MailRuleSetMapper
{
    /// <summary>Builds the rule set a declaration describes.</summary>
    /// <param name="settings">The bound declaration, already proven usable by <see cref="MailRuleDeclarationRules" />.</param>
    /// <param name="compiler">Reads each condition against the fact surface.</param>
    /// <returns>The rule set, which is empty when the declaration enables no rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a condition does not compile, which validation refuses first.</exception>
    /// <remarks>
    /// Each condition is compiled again here rather than carried over from validation. That keeps the validator free of
    /// state and free of an order it has to be called in, at the cost of one extra parse per rule per adopted
    /// configuration — which happens at startup and on a reload, never while mail is being processed.
    /// </remarks>
    public static MailRuleSet Map(MailRulesOptions settings, IMailRuleConditionCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(compiler);

        var bounds = settings.ToBounds();
        var declarations = settings.Rules
            .Where(rule => rule.Enabled)
            .Select(rule => new MailRuleDeclaration(
                rule.Name,
                rule.Condition,
                rule.StopWhenMatched,
                [.. rule.Accounts.Select(account => account.Trim())]))
            .ToArray();

        var rules = declarations
            .Select(declaration => MailRule.Create(
                declaration.Name,
                Compile(compiler, declaration, bounds),
                declaration.StopWhenMatched,
                declaration.Accounts))
            .ToArray();

        return MailRuleSet.Create(rules, MailRuleSetRevision.Create(declarations), bounds);
    }

    private static IMailRuleCondition Compile(
        IMailRuleConditionCompiler compiler,
        MailRuleDeclaration declaration,
        MailRuleConditionBounds bounds)
    {
        var compilation = compiler.Compile(declaration.Name, declaration.ConditionText, bounds);

        // Reaching this means a rule set was mapped without having been proven usable, which is a defect in the
        // composition rather than in what an operator wrote. The reasons are joined in rather than dropped, because the
        // one thing worse than the wrong rule set is one nobody can trace.
        return compilation.IsCompiled
            ? compilation.Condition
            : throw new InvalidOperationException(
                $"A mail rule set was mapped before it was validated. {string.Join(" ", compilation.Errors)}");
    }
}
