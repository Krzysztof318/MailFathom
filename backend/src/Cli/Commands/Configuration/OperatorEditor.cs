// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Runs the editor the operator's shell names, over a file this command wrote.</summary>
/// <remarks>
/// <para>
/// The editor is a requirement rather than a convenience, unlike the browser an authorization opens: the whole of the
/// editing session is what the operator does in it, and there is nothing useful to fall back to. So the command
/// refuses when neither variable is set instead of choosing an editor on their behalf — a guess that opened
/// <c>vi</c> on a machine whose operator has never used it is a worse outcome than a sentence naming the variable.
/// </para>
/// <para>
/// The editor runs attached to this terminal, which is what makes a terminal editor usable at all, and the command
/// waits for it: the file is read back when the process exits, and an editor that returns immediately having handed the
/// file to a window elsewhere reads back an unedited buffer. That is why the guidance names the wait flag a graphical
/// editor needs.
/// </para>
/// </remarks>
internal static class OperatorEditor
{
    /// <summary>The variable an operator's shell names their editor in, preferred over <see cref="EditorVariable" />.</summary>
    /// <remarks>The conventional order on every platform this ships for: <c>VISUAL</c> is the full-screen editor and <c>EDITOR</c> the line editor a script may fall back to.</remarks>
    internal const string VisualVariable = "VISUAL";

    /// <summary>The variable an operator's shell names their editor in when it names no <see cref="VisualVariable" />.</summary>
    internal const string EditorVariable = "EDITOR";

    /// <summary>Runs an editor over a file and waits for it to finish.</summary>
    /// <param name="editor">The editor as the shell names it, which may carry arguments of its own.</param>
    /// <param name="path">The file to open.</param>
    /// <returns>What became of the session, which distinguishes an editor that never started from one that ended badly.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="editor" /> or <paramref name="path" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// <para>
    /// The command line is split on spaces so that <c>code --wait</c> and <c>emacs -nw</c> work, which is what an
    /// operator writes into the variable. A path with a space in it is passed as one argument regardless, because it is
    /// added after the split rather than into it.
    /// </para>
    /// <para>
    /// The failure to start is carried out rather than swallowed. What the operating system says — no such file, not
    /// executable, no such directory — is the only thing that names why an editor an operator believes in did nothing,
    /// and it is the operator's own configured value being reported back rather than anything about this deployment.
    /// </para>
    /// </remarks>
    internal static EditingSession Run(string editor, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stated = editor.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var start = new ProcessStartInfo(stated[0]) { UseShellExecute = false };

        foreach (var argument in stated[1..])
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(path);

        try
        {
            using var session = Process.Start(start);

            if (session is null)
            {
                return EditingSession.NeverStarted("the operating system started no process for it");
            }

            session.WaitForExit();

            return session.ExitCode == 0 ? EditingSession.Finished : EditingSession.Failed;
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return EditingSession.NeverStarted(failure.Message);
        }
    }
}
