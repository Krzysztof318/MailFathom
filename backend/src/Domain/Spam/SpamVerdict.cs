// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>States what a classification concluded about one message.</summary>
/// <remarks>
/// The verdict is derived data about a message and never a statement about where the message lives. Filing it somewhere
/// is a separate decision an operator switches on, taken against the mail server rather than against the local mirror.
/// </remarks>
public enum SpamVerdict
{
    /// <summary>Nothing the message carried decided either way, which is what a message with no usable signal reaches.</summary>
    /// <remarks>
    /// It is a conclusion rather than a missing one: a message whose provider recorded nothing and whose sender
    /// authentication is absent has been classified, and the record says the signals were silent. Treating it as spam
    /// would file legitimate mail from a server that writes no headers, and treating it as clean would claim a check
    /// nobody performed.
    /// </remarks>
    Undetermined = 0,

    /// <summary>Every signal that spoke said the message is ordinary correspondence.</summary>
    NotSpam = 1,

    /// <summary>At least one signal said the message is spam, and nothing outranks it.</summary>
    Spam = 2,
}
