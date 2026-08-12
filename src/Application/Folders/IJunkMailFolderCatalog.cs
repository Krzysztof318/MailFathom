// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Answers which of an account's mapped folders is the one its server advertises for junk.</summary>
/// <remarks>
/// <para>
/// This is a fact about the mailbox rather than an opinion MailFathom formed, which is why it is read here and not
/// derived from a classification. It is true with no scanner deployed and with nothing ever classified: mail the
/// provider filed as junk is in the junk folder because the provider put it there.
/// </para>
/// <para>
/// Two paths need the answer in two shapes, for the reason
/// <see cref="IMailFolderParticipationReader" /> gives about its own two: a mailbox read narrows a table and needs the
/// whole set as a value it can put into a predicate, while classification holds one occurrence and asks about that
/// occurrence's folder. Both answers come from the same configuration, so neither can drift from the other.
/// </para>
/// </remarks>
public interface IJunkMailFolderCatalog
{
    /// <summary>Gets every account's junk folder, empty when no account maps one.</summary>
    IReadOnlyList<MailFolderIdentity> JunkFolders { get; }

    /// <summary>Reports whether one folder is the junk folder of its account.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when configuration maps that alias to the junk role.</returns>
    bool IsJunkFolder(MailAccountId accountId, MailFolderAlias folderAlias);
}
