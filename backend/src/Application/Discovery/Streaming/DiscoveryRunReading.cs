// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>One read of a run: where the run stands, and everything it has written past the cursor the reader held.</summary>
/// <param name="Running">Whether the run is still executing, which is what tells a client watching it that more is coming.</param>
/// <param name="Events">Everything after the stated cursor, in sequence order, and empty where the reader was already caught up.</param>
/// <remarks>
/// <para>
/// Both halves together are what makes one route serve the first read and every later one. A client holding nothing
/// reads from the beginning and is given the run so far; a client holding a cursor is given the tail; and either of
/// them is told in the same answer whether to expect more.
/// </para>
/// <para>
/// A run that does not exist, has been forgotten, or belongs to somebody else is not a value of this type — the read
/// reports no such run instead, which is one answer rather than a third state to read this apart from.
/// </para>
/// </remarks>
public sealed record DiscoveryRunReading(bool Running, IReadOnlyList<DiscoveryRunEvent> Events);
