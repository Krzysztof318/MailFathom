// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>One email's identity and the bounded opening of its text, as PostgreSQL answers them.</summary>
/// <param name="StoredEmailId">The email the preview belongs to, as the column holds it.</param>
/// <param name="Preview">The opening of its derived text, cut to the published bound by the query rather than here.</param>
/// <remarks>
/// The identity is the raw value rather than the domain type, because a projection EF Core translates is written from
/// what the provider can compare and the mapping back happens outside the query.
/// </remarks>
internal sealed record StoredEmailPreviewRow(Guid StoredEmailId, string Preview);
