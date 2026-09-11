// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Sessions;

namespace MailFathom.Host.Security.Sessions;

/// <summary>What one exchange did, and the token it answers with where it minted one.</summary>
/// <param name="Outcome">Whether the deployment is holding the session, and where it is not, which refusal it was.</param>
/// <param name="Token">The minted token, or <see langword="null" /> for either refusal.</param>
/// <remarks>
/// The two travel together because the route answers with three different statuses and only one of them carries a
/// token: a refusal naming the bound is tried again in a moment, and one naming a credential an operator ended is
/// answered by signing in again. A shape carrying only the token would make the route ask a second question, which is
/// the arrangement the barrier in the previous store needed and this one does not.
/// </remarks>
internal sealed record ClientSessionMint(ClientSessionMintOutcome Outcome, MintedClientSessionToken? Token);
