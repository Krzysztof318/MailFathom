// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Retrieval;

/// <summary>What a caller asks about their mail, and the mail the answer may be drawn from.</summary>
/// <param name="Text">The validated question, in the words the caller wrote it in.</param>
/// <param name="Scope">The accounts and folders the answer may be drawn from.</param>
/// <param name="AskedAt">The instant whoever asked is standing on, which every relative period in the question is resolved against.</param>
/// <remarks>
/// <para>
/// The scope belongs to the question rather than to the deployment because it is the caller's authorization expressed as
/// data: a question asked over one account must not be answerable from another, whatever the model is later told. It is
/// resolved before the run starts and applied to every retrieval the run makes.
/// </para>
/// <para>
/// The instant belongs to it for the same reason the scope does: a question naming <em>this week</em> is answered from
/// the days that person is standing on, and neither the model nor the host's own zone may decide which those are. It is
/// resolved from this deployment's clock and the asking user's recorded zone before the run starts, so every agent the
/// run composes states one anchor rather than each reaching for a clock of its own.
/// </para>
/// <para>
/// All three arrive validated and none can be built otherwise, so an entrypoint added later reaches the answering
/// port with a bounded question, a resolved scope, and an anchor, or reaches it not at all.
/// </para>
/// </remarks>
public sealed record MailQuestion(MailQuestionText Text, MailboxScope Scope, DateTimeOffset AskedAt);
