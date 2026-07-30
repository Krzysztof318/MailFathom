// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>What the ranking query returns for one matched email, before its summary is read.</summary>
/// <param name="StoredEmailId">The stable local identity of the matched email.</param>
/// <param name="RelevanceRank">What PostgreSQL scored the email's search vector against this query.</param>
/// <param name="Headline">The highlighted extracts PostgreSQL cut from the body, joined by the fragment delimiter, or <see langword="null" /> when the email has no indexed body text.</param>
/// <remarks>
/// The rank and the headline are the two values that exist only for the query that produced them, which is why they
/// travel apart from the summary rather than being folded into it: a summary describes an email, and neither of these
/// does.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailSearchHitRow(Guid StoredEmailId, float RelevanceRank, string? Headline);
