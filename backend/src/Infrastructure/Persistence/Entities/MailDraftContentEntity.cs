// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The raw MIME the current revision of one draft is held as.</summary>
/// <remarks>
/// One row per draft rather than per revision, because what a draft's author is editing is one message: keeping every
/// version would hold a message per keystroke for as long as the draft lives. The row is rewritten with the revision it
/// belongs to and in the same transaction, which is what keeps the bytes and the revision number from disagreeing.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftContentEntity
{
    public Guid MailDraftId { get; set; }

    public required byte[] RawMime { get; set; }

    public long MimeByteLength { get; set; }

    public required byte[] Sha256Hash { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public required MailDraftEntity MailDraft { get; set; }
}
