// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
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
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The append runs in the runner's finally, so anything escaping here would mask the exit code or the exception the invocation was already reporting — which is the one thing keeping a record must never do.")]
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
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Moves the current file aside once it has reached its ceiling, so the next append starts a new one.</summary>
    /// <remarks>
    /// <para>
    /// Measured before the append rather than after it, so the ceiling is what the file is allowed to reach rather than
    /// what it is allowed to exceed by one record.
    /// </para>
    /// <para>
    /// A move that fails is swallowed rather than reported, for the reason the append is shared rather than locked: two
    /// invocations can end at the same moment, and both can see a full file. The one that loses the race finds nothing
    /// left to move, and treating that as a failure would drop its record to protect a rollover the other run has
    /// already performed. Anything else that stops the move leaves the file over its ceiling until the next invocation,
    /// which is a log slightly too large rather than a record thrown away.
    /// </para>
    /// </remarks>
    private void RollOverWhenFull()
    {
        var current = new FileInfo(this.Location);

        if (!current.Exists || current.Length < MaximumBytes)
        {
            return;
        }

        try
        {
            File.Move(this.Location, this.Location + RolledSuffix, overwrite: true);
        }
        catch (Exception raced) when (raced is IOException or UnauthorizedAccessException)
        {
        }
    }
}
