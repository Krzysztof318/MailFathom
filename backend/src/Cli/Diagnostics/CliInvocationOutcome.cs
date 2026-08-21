// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Diagnostics;

/// <summary>How one invocation of the command ended.</summary>
/// <remarks>
/// Recorded beside the exit code rather than derived from it, because the third value has no exit code to derive it
/// from: an invocation that faulted never reported one, and a reader who saw only an absent number would have to guess
/// whether the command failed or the record did.
/// </remarks>
internal enum CliInvocationOutcome
{
    /// <summary>The command ran and reported success.</summary>
    Completed = 0,

    /// <summary>The command ran and reported a failure the operator can act on.</summary>
    Failed = 1,

    /// <summary>The command raised something that is a defect rather than an operator's mistake, and never reported a code.</summary>
    Faulted = 2,

    /// <summary>The operator stopped the command before it finished.</summary>
    /// <remarks>
    /// Told apart from <see cref="Faulted" /> rather than folded into it, because the two read identically in a log and
    /// mean opposite things: one is somebody pressing Ctrl+C and the other is this command being wrong.
    /// </remarks>
    Cancelled = 3,
}
