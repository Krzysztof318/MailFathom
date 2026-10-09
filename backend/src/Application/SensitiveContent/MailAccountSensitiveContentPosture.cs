// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.SensitiveContent;

/// <summary>One mail account this deployment serves, beside what the mail in it is scanned under.</summary>
/// <param name="Account">The account.</param>
/// <param name="Posture">What its mail is scanned for, what a finding in it stops, and what a derived row records.</param>
/// <remarks>
/// Read as a pair by the walk that re-derives mail written under a posture nobody runs any more, which has to judge each
/// row against its own account's stamp, and by a use case acting on one account, which reads it once and then enters it
/// with <see cref="Egress.SensitiveContentEgressGuard.ActingFor(MailAccountSensitiveContentPosture)" />.
/// </remarks>
public sealed record MailAccountSensitiveContentPosture(MailAccountId Account, SensitiveContentPosture Posture);
