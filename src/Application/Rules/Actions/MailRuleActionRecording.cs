// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Actions;

/// <summary>What recording one email's planned actions produced.</summary>
/// <param name="RecordedCount">How many mutation records the plan opened, counting one already open as recorded.</param>
/// <param name="Failures">The actions nothing was recorded for, each with the reason.</param>
/// <remarks>
/// A record already open is counted as recorded rather than as a second request, because that is what it is: the
/// identity of a rule's request is the occurrence, the rule with its revision, and the mutation, so the same rule asking
/// for the same email again finds the record it wrote the first time.
/// </remarks>
public sealed record MailRuleActionRecording(int RecordedCount, IReadOnlyList<MailRuleActionFailure> Failures)
{
    /// <summary>Gets the recording of an email whose rules asked for nothing.</summary>
    public static MailRuleActionRecording Nothing { get; } = new(0, []);
}
