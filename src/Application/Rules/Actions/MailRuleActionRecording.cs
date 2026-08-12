// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Actions;

/// <summary>What recording one email's planned actions produced.</summary>
/// <param name="Recorded">The actions a mutation record was opened for, each naming the record that carries it.</param>
/// <param name="Failures">The actions nothing was recorded for, each with the reason.</param>
/// <remarks>
/// A record already open is reported as recorded rather than as a second request, because that is what it is: the
/// identity of a rule's request is the occurrence, the rule with its revision, and the mutation, so the same rule asking
/// for the same email again finds the record it wrote the first time and the history names that same record.
/// </remarks>
public sealed record MailRuleActionRecording(
    IReadOnlyList<RecordedMailRuleAction> Recorded,
    IReadOnlyList<MailRuleActionFailure> Failures)
{
    /// <summary>Gets the recording of an email whose rules asked for nothing.</summary>
    public static MailRuleActionRecording Nothing { get; } = new([], []);

    /// <summary>Gets how many mutation records the plan opened, counting one already open as recorded.</summary>
    public int RecordedCount => this.Recorded.Count;
}
