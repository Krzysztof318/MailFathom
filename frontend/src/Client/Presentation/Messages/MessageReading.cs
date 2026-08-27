// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Messages;

/// <summary>One reading of a message list, shared by every window the pages of that reading are held in.</summary>
/// <remarks>
/// <para>
/// What tells one reading of a list from the next. A place and an arrangement say which list is being read and cannot
/// say how many times it has been: somebody asking for a folder to be read again is asking for a new reading of the
/// same list, and a page still in flight from the reading they abandoned belongs to neither the window nor the
/// indicator the new one opened with.
/// </para>
/// <para>
/// Reference identity rather than a value, because a value composed from anything the two readings share would make
/// them equal. It is created where a reading begins and carried by every window derived from it, so a page can be
/// held against the reading it was asked under rather than against a description that fits both.
/// </para>
/// </remarks>
internal sealed class MessageReading;
