// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One email an answering run retrieved, and whether the answer went on to name it.</summary>
/// <remarks>
/// <para>
/// It hangs on the local email and cascades from it, which is what makes the answering record reachable by that email's
/// deletion path rather than by a second erasure rule somebody has to remember. That is the deliberate opposite of the
/// mutation trail, whose entries survive the mail they describe because the act recorded may have <em>been</em> the
/// deletion; nothing of the sort applies to reading a message.
/// </para>
/// <para>
/// The email is reached by identifier alone, with no navigation back to the stored row, so appending one costs the read
/// of nothing: what is being recorded is that a run retrieved this identifier, and the message it names is fetched
/// through the reads that already serve it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailAnsweringAuditedEmailEntity
{
    public Guid MailAnsweringAuditEntryId { get; set; }

    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets where in the run's retrieval this email was first reached, counted from zero.</summary>
    /// <remarks>
    /// Kept because the order a run reached mail in is part of what happened, and because it survives the entry losing
    /// an email to that email's own deletion: a gap in the positions says something was read and is gone, where a
    /// shorter list would read as a shorter run.
    /// </remarks>
    public int Position { get; set; }

    public bool WasCited { get; set; }
}
