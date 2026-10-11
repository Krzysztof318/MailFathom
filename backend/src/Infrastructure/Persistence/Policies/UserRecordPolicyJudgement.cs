// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>What judging one record under the policies a write would leave it governed by answered.</summary>
/// <param name="EndpointAccess">The switches the row is to carry: what the record resolves to under the candidate policies, or, where the record cannot be resolved, the switches its row holds with every one a readable policy forces written over them. <see langword="null" /> where the record cannot be resolved and no readable policy forces either, so the row is left as it stands.</param>
/// <param name="Refusals">One sentence per reason the write would leave this record one the deployment could not serve, empty where it would not.</param>
/// <remarks>
/// A record that was already unservable before the write refuses nothing: the write did not break it, and refusing
/// every policy over a row somebody has yet to correct would leave the policy unwritable for a reason its author
/// cannot act on.
/// </remarks>
public sealed record UserRecordPolicyJudgement(UserEndpointAccess? EndpointAccess, IReadOnlyList<string> Refusals);
