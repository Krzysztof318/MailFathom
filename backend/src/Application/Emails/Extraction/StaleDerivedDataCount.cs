// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction;

/// <summary>How much of what is stored was derived under something other than its own user's current posture.</summary>
/// <param name="EmailCount">How many stored messages hold body text, passages, and vectors written under an older configuration.</param>
/// <param name="AttachmentReadingCount">How many readings of an attachment — a document's extracted text or a model's description of a picture — were written under one.</param>
/// <remarks>
/// Two numbers rather than one, because they answer different questions and cost different things to repair. A message
/// is re-derived from stored raw MIME, which is a parse and a re-embedding; a reading of an attachment is taken again by
/// the account run's attachment stage, which re-parses a stranger's file and may ask a vision provider about a picture.
/// An operator deciding whether to spend the rebuild is deciding about both, so a single total would hide the half that
/// reaches a provider.
/// <para>
/// Both are counts and nothing else. Nothing derived from a message — no subject, address, file name, or fragment of
/// text — belongs in a figure a deployment writes to its own log.
/// </para>
/// </remarks>
public sealed record StaleDerivedDataCount(int EmailCount, int AttachmentReadingCount)
{
    /// <summary>Gets whether everything stored was derived under the configuration its user now runs.</summary>
    public bool IsEmpty => this.EmailCount == 0 && this.AttachmentReadingCount == 0;
}
