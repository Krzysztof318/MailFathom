// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Correspondence;

/// <summary>What the mail a caller may read already holds about one contact.</summary>
/// <param name="Threads">The most recent conversations naming one of the contact's addresses, newest first.</param>
/// <param name="Documents">The most recent documents the contact sent, newest first.</param>
/// <remarks>
/// Both lists are computed on the read and neither is stored, so a contact record never comes to disagree with the
/// mailbox it was derived from. A list is empty where nothing in the window matched, which is the same answer a
/// deployment holding no mail for this caller gives.
/// </remarks>
public sealed record ContactCorrespondence(
    IReadOnlyList<CorrespondingThread> Threads,
    IReadOnlyList<CorrespondingDocument> Documents)
{
    /// <summary>The answer for a contact the readable mail says nothing about.</summary>
    public static ContactCorrespondence Nothing { get; } = new([], []);
}
