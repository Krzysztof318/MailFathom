// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document.Blocks;

/// <summary>A bulleted or numbered list, with whatever each of its items holds.</summary>
/// <remarks>
/// An item holds blocks rather than runs, because mail nests a list inside a list and puts a table inside an item. The
/// depth that costs is bounded during the reduction rather than by this type, so a document is bounded by what produced
/// it and a client walks what it is given.
/// </remarks>
public sealed record MailListBlock : MailDocumentBlock
{
    /// <summary>Initializes a list.</summary>
    /// <param name="ordered">Whether the list numbers its items rather than bulleting them.</param>
    /// <param name="items">The items, in the order the message wrote them.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items" /> is <see langword="null" />.</exception>
    public MailListBlock(bool ordered, IReadOnlyList<MailListItem> items)
        : base(MailDocumentBlockType.List)
    {
        ArgumentNullException.ThrowIfNull(items);

        this.Ordered = ordered;
        this.Items = items;
    }

    /// <summary>Gets whether the list numbers its items rather than bulleting them.</summary>
    public bool Ordered { get; }

    /// <summary>Gets the items, in the order the message wrote them.</summary>
    public IReadOnlyList<MailListItem> Items { get; }
}

/// <summary>One item of a list, and everything it holds.</summary>
/// <param name="Blocks">What the item holds, in reading order.</param>
public sealed record MailListItem(IReadOnlyList<MailDocumentBlock> Blocks);
