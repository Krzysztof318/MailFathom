// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Emails;

namespace MailMcp.Application.EmailContent;

/// <summary>Records that one email's locally stored content has to be fetched or read again.</summary>
/// <param name="StoredEmailId">The email whose local copy is unusable.</param>
/// <param name="Defect">What was found wrong with it.</param>
/// <remarks>
/// The request names the email and the defect and nothing else. Which folder it lives in and which occurrence it is
/// are already on its row, and copying them here would create a second, staler statement of the same identity.
/// </remarks>
public sealed record EmailContentRepairRequest(StoredEmailId StoredEmailId, EmailContentDefect Defect);
