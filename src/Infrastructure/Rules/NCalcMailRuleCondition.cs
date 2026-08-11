// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using NCalc;
using NCalc.Handlers;

namespace MailFathom.Infrastructure.Rules;

/// <summary>A condition that has been parsed and checked once, and is evaluated against one email at a time.</summary>
/// <remarks>
/// <para>
/// The parse happens when the configuration is read and never again: the tree it produced is held here and shared by
/// every evaluation, which is what keeps reading a rule set out of the cost of applying it. The tree is only read during
/// an evaluation, so one compiled condition serves however many emails a pass reaches, in whatever order.
/// </para>
/// <para>
/// What is built per evaluation is the environment, and it is built from the facts this condition actually names. A
/// fact reaches the expression as a parameter that resolves when the expression asks for it, so a branch the operators
/// short-circuit past costs nothing, and the boolean operators do short-circuit.
/// </para>
/// </remarks>
internal sealed class NCalcMailRuleCondition : IMailRuleCondition
{
    private readonly string ruleName;
    private readonly LogicalExpression parsedCondition;
    private readonly ExpressionConfiguration configuration;

    /// <summary>Holds a condition that has already been proven usable against the fact surface.</summary>
    /// <param name="ruleName">The rule the condition belongs to.</param>
    /// <param name="parsedCondition">The parsed condition, which is read and never modified.</param>
    /// <param name="configuration">The parsing and evaluation settings every evaluation of this condition runs under.</param>
    /// <param name="referencedFacts">The facts the condition names, which are the only ones its environment carries.</param>
    public NCalcMailRuleCondition(
        string ruleName,
        LogicalExpression parsedCondition,
        ExpressionConfiguration configuration,
        IReadOnlyList<MailRuleFact> referencedFacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentNullException.ThrowIfNull(parsedCondition);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(referencedFacts);

        this.ruleName = ruleName;
        this.parsedCondition = parsedCondition;
        this.configuration = configuration;
        this.ReferencedFacts = referencedFacts;
    }

    /// <inheritdoc />
    public IReadOnlyList<MailRuleFact> ReferencedFacts { get; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the expression answers with something other than a boolean.</exception>
    public async Task<bool> EvaluateAsync(MailRuleFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var environment = new ExpressionContext(
            asyncFunctions: MailRuleConditionFunctions.All,
            asyncParameters: BindFacts(facts, this.ReferencedFacts));

        var expression = new Expression(
            this.parsedCondition,
            this.configuration,
            environment,
            CultureInfo.InvariantCulture);

        // The root of a compiled condition was proven to be a boolean when it was read, so anything else here is a
        // defect in this adapter rather than in what an operator wrote. It is raised so the pass records the rule as
        // failed instead of reading an unusable value as a decision about somebody's mail.
        return await expression.EvaluateAsync(cancellationToken) is bool matched
            ? matched
            : throw new InvalidOperationException(
                $"{MailRuleConditionMessage.For(this.ruleName)} produced a value that is not a boolean.");
    }

    private static Dictionary<string, AsyncExpressionParameter> BindFacts(
        MailRuleFacts facts,
        IReadOnlyList<MailRuleFact> referencedFacts)
    {
        var bindings = new Dictionary<string, AsyncExpressionParameter>(referencedFacts.Count, StringComparer.Ordinal);

        foreach (var fact in referencedFacts)
        {
            bindings[fact.Name] = data => facts.ResolveAsync(fact, data.CancellationToken);
        }

        return bindings;
    }
}
