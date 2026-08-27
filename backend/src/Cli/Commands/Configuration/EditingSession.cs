// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>What became of an editing session, in the terms the command reports it to an operator.</summary>
/// <remarks>
/// Three endings rather than a boolean, because two of them need different advice. An editor that ran and ended badly
/// is answered with the wait flag a graphical editor needs — the operator's editor handed the file to a window
/// somewhere and returned. An editor that never started at all is answered with what the operating system said, since
/// no flag helps a name that is not on the path or a file that is not executable, and collapsing the two tells such an
/// operator that their editor ran and failed while naming a flag that will not help.
/// </remarks>
internal sealed record EditingSession
{
    private EditingSession(bool saved, string? whyItNeverStarted)
    {
        this.Saved = saved;
        this.WhyItNeverStarted = whyItNeverStarted;
    }

    /// <summary>Gets whether the editor ran and reported success, so the buffer is the command's to read back.</summary>
    internal bool Saved { get; }

    /// <summary>Gets what the operating system said when the editor could not be started, or <see langword="null" /> where it ran.</summary>
    internal string? WhyItNeverStarted { get; }

    /// <summary>Gets the session whose editor ran and reported success.</summary>
    internal static EditingSession Finished { get; } = new(saved: true, whyItNeverStarted: null);

    /// <summary>Gets the session whose editor ran and ended reporting a failure.</summary>
    internal static EditingSession Failed { get; } = new(saved: false, whyItNeverStarted: null);

    /// <summary>Reports a session whose editor could not be started at all.</summary>
    /// <param name="reason">What the operating system said, which is the only thing that names why.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is <see langword="null" />, empty, or white space.</exception>
    internal static EditingSession NeverStarted(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new(saved: false, whyItNeverStarted: reason);
    }
}
