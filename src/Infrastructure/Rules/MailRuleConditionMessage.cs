// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Rules;

/// <summary>Opens every refusal a condition can earn, so all of them name the rule the same way.</summary>
/// <remarks>
/// The rule's name is MailFathom's own configured name for it, which is what makes it safe to put in a message an
/// operator reads in a log: the condition itself may contain an address its author typed, and no refusal quotes it.
/// Where in the configuration the rule sits is added by whoever reports these, because that path belongs to the section
/// rather than to the language.
/// </remarks>
internal static class MailRuleConditionMessage
{
    /// <summary>Opens a message about one rule's condition.</summary>
    /// <param name="ruleName">The rule the condition belongs to.</param>
    /// <returns>The opening of the message, which the defect is appended to.</returns>
    public static string For(string ruleName) => $"The condition of mail rule '{ruleName}'";
}
