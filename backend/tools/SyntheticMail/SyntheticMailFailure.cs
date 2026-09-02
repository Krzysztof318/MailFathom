// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail;

/// <summary>Something the developer running the command can act on, reported as a message rather than as a stack trace.</summary>
/// <remarks>
/// The same distinction <c>mfctl</c> draws, for the same reason: a missing credential file, an address that is not an
/// address, or a count outside its bounds is one line telling somebody what to change, while anything else is a defect
/// and a stack trace is the right answer to one.
/// </remarks>
internal sealed class SyntheticMailFailure : Exception
{
    /// <summary>Initializes a failure with the message the developer reads.</summary>
    /// <param name="message">What went wrong, written for someone who can fix it.</param>
    internal SyntheticMailFailure(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a failure that wraps the one that caused it.</summary>
    /// <param name="message">What went wrong, written for someone who can fix it.</param>
    /// <param name="innerException">The failure this stands for.</param>
    internal SyntheticMailFailure(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
