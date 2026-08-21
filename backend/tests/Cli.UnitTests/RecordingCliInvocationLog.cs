// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Diagnostics;

namespace MailFathom.Cli.UnitTests;

/// <summary>A log that keeps what was appended, and refuses when a test asks it to.</summary>
/// <remarks>Refusal is part of the contract rather than a convenience: an invocation whose record could not be written must still report what the command did, and only a writer that fails proves it.</remarks>
internal sealed class RecordingCliInvocationLog : ICliInvocationLog
{
    /// <summary>Gets the records appended, in order.</summary>
    internal List<CliInvocationEntry> Appended { get; } = [];

    /// <summary>Gets or sets a value indicating whether this log accepts what it is given.</summary>
    internal bool Accepts { get; set; } = true;

    /// <inheritdoc />
    public string Location => "/dev/null/mfctl.log";

    /// <inheritdoc />
    public bool TryAppend(CliInvocationEntry entry)
    {
        if (!this.Accepts)
        {
            return false;
        }

        this.Appended.Add(entry);

        return true;
    }
}
