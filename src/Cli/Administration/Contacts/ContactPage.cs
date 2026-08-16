// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>One bounded page of a deployment's contact book, and where the walk continues.</summary>
/// <param name="Contacts">The contacts this page holds, ordered by the name's comparison form and then by identity.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the book.</param>
/// <remarks>
/// The absent cursor is the end of the walk rather than a page that happened to be short, so the command stops when the
/// cursor stops instead of comparing the count against the size it asked for. There is no command that walks the whole
/// book in one call: the operator asks for the next page, which is what keeps a listing of somebody's correspondents an
/// act rather than a side effect.
/// </remarks>
internal sealed record ContactPage(
    [property: JsonPropertyName("contacts")] IReadOnlyList<ContactRecord>? Contacts,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);
