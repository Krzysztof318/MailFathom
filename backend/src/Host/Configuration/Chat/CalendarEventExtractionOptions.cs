// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares whether text is read into the calendar events it names.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, because the reading runs against the declared chat
/// endpoint and has nowhere to send text without one. An operator who removes the chat section has removed this with
/// it, which is the honest reading of what they did.
/// </para>
/// <para>
/// <strong>Off by default, on the same reading that puts enrichment off beside it: what decides the default is who
/// spends the money and when.</strong> One switch turns on two things, and the half that matters for the default is
/// the unattended one — every message the enrichment pass reads is read a second time for the dates it names, which
/// doubles what a mailbox costs to take in. A deployment that wants the typed-description field without that cost has
/// the enrichment switch beside this one, since nothing proposes from mail on an instance that derives no marks.
/// </para>
/// <para>
/// One switch rather than two, because the two halves are one reading put to two inputs. Declaring them separately
/// would let an operator turn on a reading of a message without the reading of a sentence, which is a distinction
/// about where text came from rather than about what a deployment pays for.
/// </para>
/// <para>
/// No numbers, for the reason the derivations beside it carry none: what one reading costs is a provider call, and
/// what a deployment may spend on those in total is declared once, in <c>MailAnswering</c>. Every reading is admitted
/// against those period ceilings and counted by the same ledgers, so it competes for one allowance with the questions
/// somebody asks and declares no ceiling of its own.
/// </para>
/// </remarks>
internal sealed class CalendarEventExtractionOptions
{
    /// <summary>Gets or sets whether the dates a message names become proposals, and a typed description becomes a draft.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets which declared model this reading runs on, and empty to run it on <c>Chat:MainModel</c>.</summary>
    /// <remarks>A reading over every arriving message is the work a cheap fast model is worth declaring for, which is the whole reason a capability may name one.</remarks>
    public ChatModelReferenceOptions Model { get; set; } = new();
}
