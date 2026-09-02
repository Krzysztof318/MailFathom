// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Contacts.Collection;

/// <summary>What one synchronization run of one folder collects under.</summary>
/// <param name="Account">The account being synchronized, named by its owner and its identifier, which is what a read of its mail narrows on.</param>
/// <param name="FolderRole">The role the folder plays, or <see langword="null" /> when configuration gave it none.</param>
/// <param name="Budget">How many contacts this run may still record.</param>
/// <remarks>
/// The three are carried together because none answers anything on its own: the account decides whose settings are
/// read and whose correspondents the addresses are recorded as, the role decides which header of a message is read, and
/// the budget decides how much of what is read may be written. All three are properties of the run rather than of the
/// message that reached it, which is why the account is here rather than taken from the message — a message reached in
/// one account's run is that account's correspondence whatever occurrence identity it carries. Opening one per run is
/// also what makes the bound the run's — a value built per message would bound nothing.
/// </remarks>
public sealed record ContactCollectionRun(
    MailAccountIdentity Account,
    MailFolderSpecialUse? FolderRole,
    ContactCollectionBudget Budget);
