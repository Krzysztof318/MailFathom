// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Names one file an author attached, with the octets it is made of.</summary>
/// <param name="FileName">The name the recipient sees the file under.</param>
/// <param name="MediaType">The media type the file is declared as, which is recorded rather than sniffed.</param>
/// <param name="Content">The octets of the file.</param>
/// <remarks>
/// <para>
/// The media type is the author's statement about their own file. Deriving one from the octets would be this system
/// asserting what somebody else's file is, and deriving one from the extension would be the same guess with less
/// evidence; what a wrong media type costs is a recipient's client opening the file with the wrong application, which
/// is the author's mistake to make and to correct.
/// </para>
/// <para>
/// The octets are memory rather than a stream because the whole file is measured against the deployment's bounds before
/// any of it is composed, and a message the bounds refuse must never have been read. A future attachment source large
/// enough to matter is a streaming port rather than a wider parameter here.
/// </para>
/// </remarks>
public sealed record AuthoredEmailAttachment(string FileName, string MediaType, ReadOnlyMemory<byte> Content);
