// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>One conversation's identity and how many of its messages the scope admits, as PostgreSQL answers them.</summary>
/// <param name="EmailThreadId">The conversation the count belongs to, as the column holds it.</param>
/// <param name="MessageCount">How many of its messages the narrowed query counted.</param>
/// <remarks>
/// The identity is the raw value rather than the domain type, because a projection EF Core translates is written from
/// what the provider can group by and the mapping back happens outside the query.
/// </remarks>
internal sealed record StoredEmailThreadSizeRow(Guid EmailThreadId, int MessageCount);
