// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Cli.Credentials;

namespace MailFathom.Cli.Diagnostics;

/// <summary>Appends each invocation to a file in the operator's own directory, one record per line.</summary>
/// <remarks>
/// <para>
/// The file is the only durable record of what the command did. <c>mfctl</c> holds no exporter and opens no span, so
/// nothing else on the machine answers <em>what did I run against that deployment, and how did it end</em> once the
/// terminal's scrollback is gone.
/// </para>
/// <para>
/// It is created readable by its owner alone, beside the credential store, and it is bounded: past
/// <see cref="MaximumBytes" /> the current file is moved aside and a new one started, so the log occupies at most twice
/// that and never grows without limit on a machine somebody administers daily.
/// </para>
/// </remarks>
internal sealed class FileCliInvocationLog : ICliInvocationLog
{
    /// <summary>The size at which the current file is moved aside and a new one started.</summary>
    internal const long MaximumBytes = 1024 * 1024;

    /// <summary>The name the file carries in the operator's directory.</summary>
    internal const string FileName = "mfctl.log";

    /// <summary>What is appended to the name of the file moved aside, which the next rollover overwrites.</summary>
    /// <remarks>One rather than a numbered series, because a series is a retention policy and nothing here is in a position to decide one for somebody's machine.</remarks>
    internal const string RolledSuffix = ".1";

    /// <summary>Initializes the log over a file path.</summary>
    /// <param name="location">Where the log is written; its directory is created when the first record is appended.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="location" /> is <see langword="null" />.</exception>
    internal FileCliInvocationLog(string location)
    {
        ArgumentNullException.ThrowIfNull(location);

        this.Location = location;
    }

    /// <inheritdoc />
    public string Location { get; }

    /// <summary>Reports where the log lives for the operator running the command.</summary>
    /// <returns>The absolute path of the log file.</returns>
    internal static string DefaultPath() => Path.Combine(OperatorDirectory.Resolve(), FileName);

    /// <inheritdoc />
    public bool TryAppend(CliInvocationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            OwnerOnlyStorage.CreateDirectoryFor(this.Location);
            this.RollOverWhenFull();

            // Serialized to bytes and written in one call, because two invocations may end at the same moment and an
            // append that reached the file in pieces would interleave them into lines neither run wrote.
            var line = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(entry, CliInvocationLogJsonContext.Default.CliInvocationEntry) + '\n');

            using var contents = OwnerOnlyStorage.OpenForAppending(this.Location);

            contents.Write(line);

            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Moves the current file aside once it has reached its ceiling, so the next append starts a new one.</summary>
    /// <remarks>
    /// Measured before the append rather than after it, so the ceiling is what the file is allowed to reach rather than
    /// what it is allowed to exceed by one record. A file that vanished between the two steps is not a failure: the
    /// append that follows creates it.
    /// </remarks>
    private void RollOverWhenFull()
    {
        var current = new FileInfo(this.Location);

        if (!current.Exists || current.Length < MaximumBytes)
        {
            return;
        }

        File.Move(this.Location, this.Location + RolledSuffix, overwrite: true);
    }
}
