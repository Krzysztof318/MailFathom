// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Carries the page of an owner's outbox a request named, or the reason it named none.</summary>
/// <remarks>
/// It is <see cref="OutboxQueryResult" /> after the query has been run rather than beside it, because an owner-facing
/// reading resolves the account it may narrow to and reads the page in one act: splitting the two would put the
/// scoping in whatever called it, which is where an owner-facing read must never decide.
/// </remarks>
/// <param name="Page">The page, present exactly when <paramref name="Outcome" /> is <see cref="OutboxQueryOutcome.Accepted" />.</param>
/// <param name="Outcome">What happened, which for a refusal is what the caller has to change.</param>
public sealed record OwnerOutboxPageResult(OutboxPage? Page, OutboxQueryOutcome Outcome);
