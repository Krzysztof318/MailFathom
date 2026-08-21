// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Repair;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class EmailContentRepairRequestEntity
{
    public Guid StoredEmailId { get; set; }

    public EmailContentDefect Defect { get; set; }

    /// <summary>Gets or sets when this email's content was first found unusable and never since served.</summary>
    /// <remarks>
    /// It survives a later request for the same email, so an outstanding defect keeps saying how long it has been
    /// outstanding. A repair that clears the row is what resets it; a second read of the same damaged message is not.
    /// </remarks>
    public DateTimeOffset FirstRequestedAt { get; set; }

    public DateTimeOffset LastRequestedAt { get; set; }

    /// <summary>Gets or sets how many times a read has found this email's content unusable.</summary>
    /// <remarks>
    /// The count is what separates a message read once while a write was in flight from one that has failed a hundred
    /// reads and needs a person to look at it.
    /// </remarks>
    public int RequestCount { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }
}
