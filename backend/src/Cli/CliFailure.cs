// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

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

    /// <summary>Initializes a failure the deployment answered with, carrying the status it answered.</summary>
    /// <param name="message">What went wrong, written for someone who can fix it.</param>
    /// <param name="status">The status the deployment answered.</param>
    internal CliFailure(string message, HttpStatusCode status)
        : base(message)
    {
        this.Status = status;
    }

    /// <summary>Gets the status the deployment answered, or <see langword="null" /> where the failure never reached one.</summary>
    /// <remarks>
    /// Carried so a command can tell one refusal from another where the difference changes what it does — a grant the
    /// token does not hold is not the same fact as a deployment nothing could reach. A command that treats every
    /// failure alike ignores it, which is the ordinary case.
    /// </remarks>
    internal HttpStatusCode? Status { get; }

    /// <summary>Initializes a failure that wraps the one that caused it.</summary>
    /// <param name="message">What went wrong, written for someone who can fix it.</param>
    /// <param name="innerException">The failure this stands for.</param>
    internal CliFailure(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
