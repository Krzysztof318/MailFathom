// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Diagnostics;

/// <summary>Where the record of an invocation is appended.</summary>
/// <remarks>
/// A seam so a test can assert what a command would have written without reaching the operator's own directory, and so
/// the runner can be driven against a writer that refuses. It is deliberately not an asynchronous contract: the append
/// happens as the process is about to exit, where starting work nobody will await buys nothing.
/// </remarks>
internal interface ICliInvocationLog
{
    /// <summary>Gets where the log is written, so a message about a record that could not be appended names the file.</summary>
    string Location { get; }

    /// <summary>Appends one record.</summary>
    /// <param name="entry">What the invocation did.</param>
    /// <returns><see langword="true" /> when the record was written, and <see langword="false" /> when it could not be.</returns>
    /// <remarks>
    /// Reports rather than raises, because the command's job is the command: a home directory that is read-only, a full
    /// disk, or a log an operator deleted the directory of must not turn a successful invocation into a failed one.
    /// </remarks>
    bool TryAppend(CliInvocationEntry entry);
}
