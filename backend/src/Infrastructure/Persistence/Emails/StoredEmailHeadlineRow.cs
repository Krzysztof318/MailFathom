// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The extracts PostgreSQL cut from one message's body for one query.</summary>
/// <param name="StoredEmailId">The stable local identity of the email the extracts came from.</param>
/// <param name="Headline">The highlighted extracts joined by the fragment delimiter, or <see langword="null" /> when the email has no indexed body text.</param>
/// <remarks>
/// The row carries the headline and nothing else about the message, which is what keeps the body column out of every
/// result set that crosses this boundary: <c>ts_headline</c> is projected, the text it cut from is not.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailHeadlineRow(Guid StoredEmailId, string? Headline);
