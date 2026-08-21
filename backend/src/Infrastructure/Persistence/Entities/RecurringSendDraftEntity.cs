// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class RecurringSendDraftEntity
{
    public Guid RecurringSendId { get; set; }

    /// <summary>Gets or sets the composed message every occurrence of this declaration is made from.</summary>
    /// <remarks>
    /// It is a draft rather than a message: nothing transmits these bytes, and what each occasion sends is composed
    /// from them with an identity and a date of its own. Keeping it in a table of its own is what stops a query over
    /// the declarations from loading every one of their messages.
    /// </remarks>
    public required byte[] DraftMime { get; set; }

    public long DraftByteLength { get; set; }

    public required byte[] Sha256Hash { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public required RecurringSendEntity RecurringSend { get; set; }
}
