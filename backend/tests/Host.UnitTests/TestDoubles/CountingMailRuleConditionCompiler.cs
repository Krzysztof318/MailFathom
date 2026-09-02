// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Infrastructure.Rules;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Reads conditions exactly as the real compiler does, and counts how often it was asked to.</summary>
/// <remarks>
/// The real compiler is used underneath rather than substituted, because what these tests are about is the
/// configuration around it: which rules reach it, in what order, and how often a published configuration is read. A
/// substitute would let a test pass while the section handed the compiler something it would have refused.
/// </remarks>
internal sealed class CountingMailRuleConditionCompiler : IMailRuleConditionCompiler
{
    private readonly NCalcMailRuleConditionCompiler compiler = new();

    /// <summary>Gets how many conditions have been read.</summary>
    public int CompileCount { get; private set; }

    /// <inheritdoc />
    public MailRuleConditionCompilation Compile(
        string ruleName,
        string? conditionText,
        MailRuleConditionBounds bounds)
    {
        this.CompileCount++;

        return this.compiler.Compile(ruleName, conditionText, bounds);
    }
}
