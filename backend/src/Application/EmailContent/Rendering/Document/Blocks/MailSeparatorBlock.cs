// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>A horizontal rule the message drew between two parts of itself.</summary>
/// <remarks>
/// It carries nothing, because a rule is the whole of what it says. A pane draws it with its own separator brush rather
/// than with anything the sender asked for, which is what keeps a message from drawing a line the reader's theme cannot
/// see.
/// </remarks>
public sealed record MailSeparatorBlock : MailDocumentBlock
{
    /// <summary>Initializes a separator.</summary>
    public MailSeparatorBlock()
        : base(MailDocumentBlockType.Separator)
    {
    }
}
