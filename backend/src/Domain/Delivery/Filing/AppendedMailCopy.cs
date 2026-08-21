// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Domain.Delivery.Filing;

/// <summary>What a mail server said about the copy it accepted into a folder.</summary>
/// <param name="Placement">Where the folder put the copy, when the server named it.</param>
/// <param name="InternetMessageId">The <c>Message-ID</c> the appended bytes carry, or <see langword="null" /> when they carry none.</param>
/// <remarks>
/// <para>
/// The two answers are both here because the join uses whichever it can get. RFC 4315's <c>APPENDUID</c> names the new
/// occurrence exactly and is what a server advertising <c>UIDPLUS</c> returns; a server that advertises none accepts
/// the same message and says nothing about where it went, and the identity in the message's own headers is then all
/// there is to recognize it by later.
/// </para>
/// <para>
/// The identity is read off the bytes that were appended rather than assumed from what was composed, so what is
/// recorded is what a mail server will report back. Nothing else about the message crosses this boundary.
/// </para>
/// </remarks>
public sealed record AppendedMailCopy(RemoteEmailPlacement Placement, string? InternetMessageId);
