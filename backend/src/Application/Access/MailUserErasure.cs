// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access;

/// <summary>What one attempt at erasing a user did, or the account that stopped it doing anything.</summary>
/// <param name="UserErased">
/// Whether a user record was there to remove. False beside no refusal is the repeat of an erasure that already ran,
/// which is the no-op the caller asked for rather than a failure.
/// </param>
/// <param name="UnquiescedAccount">
/// The mail account the erasure could not establish was still, or <see langword="null" /> when every account it was
/// about to delete was covered for the whole transaction. It is set only where nothing was written.
/// </param>
/// <remarks>
/// The refusal exists because what the caller stopped before it asked and what the transaction finds are two readings
/// taken at different instants. Between them an assignment can end, leaving an account solely this user's that no hold
/// covers, and a job already queued can be claimed. Either makes the deletion one that would commit against a live
/// writer, and the answer to that is to write nothing and say which account it was — half an erasure cannot be undone,
/// and a caller told which mailbox is still busy can ask again.
/// </remarks>
public readonly record struct MailUserErasure(bool UserErased, Guid? UnquiescedAccount)
{
    /// <summary>Gets whether the erasure was abandoned with nothing written.</summary>
    public bool Refused => this.UnquiescedAccount is not null;
}
