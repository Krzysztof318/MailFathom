// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>What the ranking query returns for one matched email, before anything is read about the email itself.</summary>
/// <param name="StoredEmailId">The stable local identity of the matched email.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, which with the identity is its place in the timeline order.</param>
/// <param name="RelevanceRank">What PostgreSQL scored the email's search vector against this query.</param>
/// <remarks>
/// The rank exists only for the query that produced it, which is why it travels apart from the summary rather than
/// being folded into it: a summary describes an email, and a rank describes a query's opinion of one. The received
/// timestamp is here because the timeline order breaks rank ties and fusion ties alike, and reading it back for a
/// candidate that never reaches the window would be a second query for an ordering decision already made.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailSearchHitRow(Guid StoredEmailId, DateTimeOffset? ReceivedAt, float RelevanceRank);
