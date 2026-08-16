// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Everything this deployment holds about one person in its contact book, as of one instant.</summary>
/// <param name="Contact">The complete record: the name, every address, which one is preferred, the note, the origin, and both timestamps.</param>
/// <param name="ProducedAt">When the export was produced, which is what dates the answer an owner is handed.</param>
/// <remarks>
/// <para>
/// The data-subject access path, named as one rather than left as "read the contact and print it". Today the whole of
/// what the book holds about a person is the contact record itself, so this carries that and the instant it was taken;
/// it is the type a later record derived from a contact — the mail an address was collected from, above all — joins,
/// which is why the obligation has a shape here from the first commit instead of being discovered when there is
/// something to add to it.
/// </para>
/// <para>
/// Rendering is deliberately not here. What an owner reads is a surface's decision, and every surface over the book
/// renders the same complete record rather than choosing which parts of a person to hand back.
/// </para>
/// </remarks>
public sealed record ContactExport(Contact Contact, DateTimeOffset ProducedAt);
