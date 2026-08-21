// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>What a caller asked one page of the contact book for, before any of it has been checked.</summary>
/// <remarks>
/// The text a caller wrote travels as text, so every bound the book holds is applied in one place — the use case — and
/// an entrypoint added later cannot reach the store having checked fewer of them. What an adapter still owns is the
/// shape: turning a published enumeration into the origin the book records, which is a translation rather than a rule.
/// </remarks>
public sealed record ContactPageRequest
{
    /// <summary>Gets the origin the page is narrowed to, or <see langword="null" /> for the whole book.</summary>
    public ContactOrigin? Origin { get; init; }

    /// <summary>Gets the text a contact must carry in its name or in one of its addresses, or <see langword="null" /> for no narrowing.</summary>
    public string? Search { get; init; }

    /// <summary>Gets how many contacts the page may hold, or <see langword="null" /> for the book's default.</summary>
    public int? PageSize { get; init; }

    /// <summary>Gets the cursor a previous page returned, or <see langword="null" /> for the first page.</summary>
    public string? Cursor { get; init; }
}
