// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.SensitiveContent;

/// <summary>One owner this deployment serves, beside what their mail is scanned under.</summary>
/// <param name="Owner">The owner.</param>
/// <param name="Posture">What their mail is scanned for, what a finding in it stops, and what a derived row records.</param>
/// <remarks>
/// The pair exists for the one consumer that asks about every owner at once rather than about the owner in front of
/// it: the walk that re-derives mail written under a posture nobody runs any more has to judge each row against its
/// own owner's stamp, and a query cannot ask a port row by row. Everything else resolves the owner it is acting for
/// and calls <see cref="ISensitiveContentPostures.ForOwner" />.
/// </remarks>
public sealed record OwnerSensitiveContentPosture(MailOwnerId Owner, SensitiveContentPosture Posture);
