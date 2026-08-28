// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>Why one exchange with a deployment did not produce an answer.</summary>
/// <remarks>
/// Five cases rather than a status code, because what a screen does about them differs and nothing else about the
/// answer does: a refused credential asks the person to sign in again, an unreachable deployment asks them to check
/// their connection, a timeout is worth retrying unchanged, a refused request is something the client asked for and can
/// therefore ask differently, and an unusable answer is a defect somebody has to be told about rather than something
/// the person can act on.
/// </remarks>
public enum DeploymentFailureReason
{
    /// <summary>The deployment or its authorization server refused what was presented.</summary>
    CredentialRefused = 0,

    /// <summary>Nothing answered within the configured timeout.</summary>
    TimedOut = 1,

    /// <summary>The deployment could not be reached at all.</summary>
    Unreachable = 2,

    /// <summary>Something answered, but not with what the contract says it answers with.</summary>
    Unusable = 3,

    /// <summary>The deployment understood the request and would not serve it as asked.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Unusable" /> because the two lead to opposite acts. This is the client's own request
    /// being wrong rather than the deployment being wrong, so the caller that composed it is the one that can ask
    /// differently — a message list handed a cursor issued for a list somebody has since re-sorted drops the cursor and
    /// reads from the leading end, where reporting a defect would strand the screen on a value nobody typed.
    /// </remarks>
    RequestRefused = 4,
}

/// <summary>One exchange with a deployment that did not produce an answer, stated as something a screen can act on.</summary>
/// <remarks>
/// <para>
/// Every failure this assembly raises is one of these, so a model calling it catches one type rather than
/// <see cref="HttpRequestException" />, <see cref="TaskCanceledException" />, and <see cref="System.Text.Json.JsonException" />
/// separately and guessing which of them meant what.
/// </para>
/// <para>
/// The message never carries the deployment's answer back verbatim. What a deployment returns is either personal data
/// under the root instructions or text from a machine this process does not own, and a screen showing either would be
/// a way to put an attacker's words in MailFathom's own voice.
/// </para>
/// </remarks>
public sealed class DeploymentFailure : Exception
{
    /// <summary>Initializes a failure stating why the exchange did not produce an answer.</summary>
    /// <param name="reason">What went wrong, in the terms a caller decides on.</param>
    /// <param name="message">What to tell the person.</param>
    public DeploymentFailure(DeploymentFailureReason reason, string message)
        : base(message) => this.Reason = reason;

    /// <summary>Initializes a failure over the transport or parsing failure behind it.</summary>
    /// <param name="reason">What went wrong, in the terms a caller decides on.</param>
    /// <param name="message">What to tell the person.</param>
    /// <param name="innerException">The failure this one was raised for.</param>
    public DeploymentFailure(DeploymentFailureReason reason, string message, Exception innerException)
        : base(message, innerException) => this.Reason = reason;

    /// <summary>Initializes a failure with no stated reason, which no code here does.</summary>
    /// <remarks>Present because the analyzers ask an exception type for the three standard constructors; the reason defaults to the case a caller can least act on.</remarks>
    public DeploymentFailure()
        : this(DeploymentFailureReason.Unusable, "The deployment did not produce an answer.")
    {
    }

    /// <summary>Initializes a failure with no stated reason, which no code here does.</summary>
    /// <param name="message">What to tell the person.</param>
    public DeploymentFailure(string message)
        : this(DeploymentFailureReason.Unusable, message)
    {
    }

    /// <summary>Initializes a failure with no stated reason, which no code here does.</summary>
    /// <param name="message">What to tell the person.</param>
    /// <param name="innerException">The failure this one was raised for.</param>
    public DeploymentFailure(string message, Exception innerException)
        : this(DeploymentFailureReason.Unusable, message, innerException)
    {
    }

    /// <summary>Gets what went wrong, which is what a caller decides on.</summary>
    public DeploymentFailureReason Reason { get; }
}
