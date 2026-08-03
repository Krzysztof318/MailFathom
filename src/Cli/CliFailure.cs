// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli;

/// <summary>Something the operator can act on, reported as a message rather than as a stack trace.</summary>
/// <remarks>
/// The command is run by a person at a terminal, so a failure they caused — an address that is not a URL, a credential
/// the service refused, a file that cannot be written — is one line telling them what to change. Anything not of this
/// kind is left to propagate, because a stack trace is the right answer to a defect.
/// </remarks>
internal sealed class CliFailure : Exception
{
    /// <summary>Initializes a failure with the message the operator reads.</summary>
    /// <param name="message">What went wrong, written for someone who can fix it.</param>
    internal CliFailure(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a failure that wraps the one that caused it.</summary>
    /// <param name="message">What went wrong, written for someone who can fix it.</param>
    /// <param name="innerException">The failure this stands for.</param>
    internal CliFailure(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
