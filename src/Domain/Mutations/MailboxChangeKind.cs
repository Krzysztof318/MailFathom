// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>Names one kind of change to a mailbox that synchronization discovers and rule evaluation would act on.</summary>
/// <remarks>
/// <para>
/// The three kinds are what a mail server can tell MailFathom has happened to a message that is already stored: it is
/// somewhere it was not, it is no longer where it was, or its <c>\Seen</c> flag stands elsewhere. Every mutation
/// MailFathom is permitted to make arrives back as one of them, which is what lets provenance be decided once for all
/// four rather than per mutation.
/// </para>
/// <para>
/// The kind says what changed and never who caused it. That second question is answered from the durable mutation
/// record, and keeping the two apart is the point: a person moving mail by hand produces exactly the same kind as a
/// rule that files it, and only the record tells them apart.
/// </para>
/// </remarks>
public enum MailboxChangeKind
{
    /// <summary>A folder holds an occurrence of an email it did not hold before.</summary>
    EmailAppearedInFolder = 0,

    /// <summary>A folder no longer holds an occurrence of an email it did hold.</summary>
    EmailLeftFolder = 1,

    /// <summary>The remote <c>\Seen</c> flag of a stored email stands at a different value than the one last observed.</summary>
    SeenStateChanged = 2,
}
