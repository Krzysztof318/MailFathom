// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>Names one kind of change to a mailbox that synchronization discovers and rule evaluation would act on.</summary>
/// <remarks>
/// <para>
/// The kinds are what a mail server can tell MailFathom has happened to a message that is already stored: it is
/// somewhere it was not, it is no longer where it was, or one of the values a <c>STORE</c> writes stands elsewhere.
/// Every mutation MailFathom is permitted to make arrives back as one of them, which is what lets provenance be decided
/// once for the whole set rather than per mutation.
/// </para>
/// <para>
/// A flag has one kind per value rather than one for every flag together, because the question a suppression answers is
/// asked per value: a record that set <c>\Flagged</c> accounts for the star standing where it put it and for nothing
/// about whether the message was read. The keywords are the one exception and are a single kind, because the three
/// keyword mutations write one set between them and a server reports that set whole.
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

    /// <summary>The remote <c>\Flagged</c> flag of a stored email stands at a different value than the one last observed.</summary>
    FlaggedStateChanged = 3,

    /// <summary>The keywords a stored email carries are no longer the ones last observed on it.</summary>
    /// <remarks>The comparison is of the whole set, because that is what a server reports and what a replacement writes; which keyword joined or left it is not a distinction any mutation asks for.</remarks>
    KeywordsChanged = 4,
}
