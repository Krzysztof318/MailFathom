// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.CalendarEvents;

/// <summary>One text put to the extraction agent, as the deployment reading it composes and reads it.</summary>
/// <remarks>
/// The agent reads two kinds of text under one instruction, and the two differ in nothing a scenario has to branch on:
/// a turn, the instant its relative days resolve against, and how many events the reading behind it keeps. Carrying
/// exactly those three is what lets a message and a typed sentence be one case shape.
/// </remarks>
/// <param name="Text">The turn, composed by the composer a deployment composes it with.</param>
/// <param name="Anchor">The instant the text belongs to, whose offset the reading resolves every written time in.</param>
/// <param name="MaximumEvents">How many events the reading behind this turn keeps.</param>
internal sealed record CalendarEventExtractionTurn(string Text, DateTimeOffset Anchor, int MaximumEvents);
