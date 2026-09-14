// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.SensitiveContent;

/// <summary>One mail account this deployment serves, beside what the mail in it is scanned under.</summary>
/// <param name="Account">The account.</param>
/// <param name="Posture">What its mail is scanned for, what a finding in it stops, and what a derived row records.</param>
/// <remarks>
/// The pair exists for the one consumer that asks about every account at once rather than about the account in front
/// of it: the walk that re-derives mail written under a posture nobody runs any more has to judge each row against its
/// own account's stamp, and a query cannot ask a port row by row. Everything else resolves the account the mail
/// belongs to and calls <see cref="ISensitiveContentPostures.ForAccount" />.
/// </remarks>
public sealed record MailAccountSensitiveContentPosture(MailAccountId Account, SensitiveContentPosture Posture);
