// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>How much of a conversation the stored state was derived from.</summary>
/// <remarks>
/// Stored beside the statements rather than inferred from their absence, because the two states a reader must be able
/// to tell apart look identical otherwise: a conversation there was nothing to say about, and one this deployment
/// declined to read because it is longer than one derivation may take in. Summarizing the part that fits and saying
/// nothing about the rest is the third option, and it is refused — a statement drawn from a third of an exchange reads
/// exactly like one drawn from all of it.
/// </remarks>
public enum ThreadStateCoverage
{
    /// <summary>The derivation was shown every message of the conversation this caller may see.</summary>
    WholeThread = 0,

    /// <summary>The conversation is longer than one derivation may take in, so nothing was derived from any part of it.</summary>
    ThreadTooLarge = 1,
}
