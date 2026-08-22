// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Screening;

/// <summary>Reads back what a composed outgoing message says, so it can be screened before it is written down.</summary>
/// <remarks>
/// <para>
/// A port of its own rather than a reuse of the reader that renders stored mail, because the two are asked about
/// different artifacts under different rules. That one is handed a message a stranger sent, parses it under structural
/// limits written for hostile input, sanitizes its markup, and bounds what a reader may be shown. This one is handed
/// bytes this deployment composed seconds earlier and wants them whole: markup is screened as it will be transmitted
/// rather than as a browser would be allowed to render it, because a credential inside an attribute a sanitizer strips
/// still leaves in the message.
/// </para>
/// <para>
/// It exists so the MIME library stays inside its adapter. Implementations reach no network and no database: they are
/// handed the bytes a composition produced or a draft stored, and reading them can therefore neither transmit anything
/// nor touch a remote flag.
/// </para>
/// <para>
/// A parse failure is not modelled, and that is a statement about where the bytes come from rather than an omission.
/// Every message reaching this port was composed by this deployment's own composer — a send from what an author wrote,
/// an occasion from a stored declaration, a draft from either — so bytes that will not parse are a defect in that
/// composer, and an implementation lets the failure travel as one.
/// </para>
/// </remarks>
public interface IOutgoingMailTextReader
{
    /// <summary>Reads the subject and the body representations out of one composed message.</summary>
    /// <param name="rawMime">The RFC 822 bytes that will be stored and transmitted.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <returns>What the message says in words.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    Task<OutgoingMailText> ReadAsync(ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken);
}
