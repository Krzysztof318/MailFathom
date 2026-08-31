// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Which of the two things a document being bound is: a record somebody is writing, or one already held.</summary>
/// <remarks>
/// <para>
/// Almost every rule is the same in both directions, which is why one binder serves them: a record that would be
/// refused as a candidate must not be accepted as a row. The exception is the rule that judges a record against
/// something outside it that the operator can change afterwards.
/// </para>
/// <para>
/// A scanning block is that case. It is judged against the deployment's own section, and an operator switching a
/// scanner on deployment-wide or widening what stops outgoing mail — the tightening this whole feature exists to keep
/// available — turns every already-accepted record that asked for less into one that reads as a loosening. Refusing
/// those at the next start would refuse the start itself, for every owner, over a change nobody can now undo from
/// inside: the surface an owner would rewrite their record through is behind the gate that is failing. So a stored
/// record's scanning block is composed rather than refused, which takes the stricter of the two exactly as it would
/// have.
/// </para>
/// </remarks>
internal enum OwnerRecordArrival
{
    /// <summary>A record being written, judged by every rule including what the deployment allows it to ask for.</summary>
    BeingWritten = 0,

    /// <summary>A record this deployment already holds, whose scanning block the composition takes the stricter of.</summary>
    AlreadyHeld = 1,
}
