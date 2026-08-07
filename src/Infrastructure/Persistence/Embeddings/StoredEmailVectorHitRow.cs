// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>What the vector ranking query returns for one email, before anything is read about the email itself.</summary>
/// <param name="StoredEmailId">The stable local identity of the ranked email.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, which with the identity is its place in the timeline order.</param>
/// <param name="Distance">How far the email's nearest embedded passage sits from the query, smaller being nearer.</param>
/// <remarks>
/// The distance is expressed in whatever units the profile's metric uses and is comparable only within one query. It is
/// carried out of the adapter for the ordering it produced and for nothing else: no caller publishes it, because a
/// number whose scale changes with the embedding model would tell a client something it could not act on.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailVectorHitRow(Guid StoredEmailId, DateTimeOffset? ReceivedAt, double Distance);
