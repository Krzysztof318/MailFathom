// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>One bounded page of the contact book, and where the walk continues.</summary>
/// <param name="Contacts">The contacts this page holds, ordered by the name's comparison form and then by identity.</param>
/// <param name="NextCursor">The boundary the following page reads beyond, or <see langword="null" /> when this page reached the end of the book.</param>
public sealed record ContactPage(IReadOnlyList<Contact> Contacts, ContactCursor? NextCursor);
