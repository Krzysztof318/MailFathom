// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Administration;

/// <summary>How many attachments one recorded reason accounts for.</summary>
/// <param name="Outcome">The reason, as the port that answered publishes it: an extraction outcome or a description refusal.</param>
/// <param name="AttachmentCount">How many attachments carry that reason.</param>
/// <remarks>
/// <para>
/// The aggregate that lets an operator tell "nothing left to do" from "a lot was skipped" without opening a single
/// message. It is a reason and a count and nothing else: which messages carried them, what the files were called, and
/// what they said are all mail, and none of them is here.
/// </para>
/// <para>
/// The reason is text rather than an enumeration for the same reason it is text on the stored reading — two closed sets
/// answer it, one per port, and neither shares a member name with the other, so a third enumeration copying both would
/// be one more place for a member to go missing. A deployment newer than the tool that reads it may report a word the
/// tool has no sentence for, which is repeated verbatim rather than replaced by a guess.
/// </para>
/// </remarks>
public sealed record AttachmentSkipCount(string Outcome, long AttachmentCount);
