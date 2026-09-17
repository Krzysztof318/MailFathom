// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials;

namespace MailFathom.Cli.Editing;

/// <summary>Puts a document the deployment handed over in front of the operator, and reports what they left to commit.</summary>
/// <remarks>
/// <para>
/// Three commands fetch a JSON document with the version it was read at, let the operator change it whole, and commit
/// it against that version: the deployment's own persisted configuration, one user's record, and one mail account's
/// declaration. Everything between the fetch and the commit is the same act, so it is written once — which is also what
/// keeps the guidance an operator reads when their editor does not cooperate, and the YAML view an operator may ask
/// for instead of JSON, from existing in several versions that drift apart.
/// </para>
/// <para>
/// What each command keeps is the part that is genuinely its own: which document it fetched, what the version guard is
/// composed over, and what a refusal means. This decides nothing about any of those and never commits anything.
/// </para>
/// </remarks>
internal static class EditorDrivenDocument
{
    /// <summary>Finds the editor the operator's shell names, refusing rather than choosing one for them.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The editor as the shell names it, which may carry arguments of its own.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when neither variable names an editor.</exception>
    internal static string EditorNamedByTheShell(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Variable(OperatorEditor.VisualVariable) is { Length: > 0 } visual ? visual
            : context.Variable(OperatorEditor.EditorVariable) is { Length: > 0 } editor ? editor
            : throw new CliFailure(
                $"No editor is named for this shell, so there is nothing to open the document in. Set ${OperatorEditor.VisualVariable} or ${OperatorEditor.EditorVariable} — a graphical editor needs the flag that makes it wait, such as 'code --wait'.");
    }

    /// <summary>Opens a document in the operator's editor and reads back what they saved.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="editor">The editor <see cref="EditorNamedByTheShell" /> found.</param>
    /// <param name="buffered">What the session's directory and its buffer are named after, as a filename segment.</param>
    /// <param name="describedAs">What the deployment holds, as the sentence reporting an abandoned session names it.</param>
    /// <param name="document">The document as the deployment handed it over.</param>
    /// <param name="view">The syntax the operator edits the document in.</param>
    /// <param name="cancellationToken">Cancels the read back.</param>
    /// <returns>The JSON document the operator left to commit, or <see langword="null" /> where the session asked for no change.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the buffer could not be written or read back, the editor did not finish, or a YAML buffer could not be read as JSON.</exception>
    /// <remarks>
    /// <para>
    /// The buffer is removed however the session ended, including when the commit that follows throws, which is why the
    /// caller's commit stays outside: a document left on disk is what this is careful about, and a caller that had to
    /// remember to clean up would be the one place it is forgotten.
    /// </para>
    /// <para>
    /// A YAML buffer this command refuses is the one exception, and it is kept. The refusal is local — nothing reached the
    /// deployment — so the edit exists nowhere else, and discarding it would throw away an operator's work over a typo.
    /// It stays in the session's own directory, readable by its user alone, and the failure names the path.
    /// </para>
    /// </remarks>
    internal static async Task<string?> OpenAsync(
        CliContext context,
        string editor,
        string buffered,
        string describedAs,
        string document,
        DocumentView view,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(buffered);
        ArgumentException.ThrowIfNullOrWhiteSpace(describedAs);
        ArgumentNullException.ThrowIfNull(document);

        var session = Path.Combine(Path.GetTempPath(), $"mailfathom-{buffered}-{Guid.NewGuid():N}");
        var buffer = Path.Combine(session, buffered + ExtensionOf(view));
        var shown = Show(document, view);
        var keepSession = false;

        try
        {
            Open(session, buffer, shown);

            if (context.Edit(editor, buffer) is { Saved: false } ended)
            {
                throw new CliFailure(WhyNothingWasWritten(editor, ended));
            }

            var saved = await ReadBackAsync(buffer, cancellationToken);

            if (Abandoned(context, describedAs, shown, saved))
            {
                return null;
            }

            if (view == DocumentView.Json)
            {
                return saved;
            }

            string? committed;

            try
            {
                committed = YamlDocumentView.ReadBack(saved);
            }
            catch (FormatException refusal)
            {
                keepSession = true;

                throw EditKeptAfterRefusal(refusal, buffer);
            }

            return AbandonedAfterConversion(context, describedAs, document, committed) ? null : committed;
        }
        finally
        {
            if (!keepSession)
            {
                Discard(session);
            }
        }
    }

    /// <summary>Renders a document in the syntax an operator asked to see it in.</summary>
    /// <param name="document">The document as the deployment handed it over.</param>
    /// <param name="view">The syntax to show it in.</param>
    /// <returns>The document as the operator reads it.</returns>
    /// <exception cref="CliFailure">Thrown when a YAML view is asked of something that is not JSON.</exception>
    internal static string Show(string document, DocumentView view) =>
        view == DocumentView.Yaml ? YamlDocumentView.Render(document) : document;

    /// <summary>Names the extension a buffer carries, so the operator's editor highlights the syntax it holds.</summary>
    private static string ExtensionOf(DocumentView view) => view == DocumentView.Yaml ? ".yaml" : ".json";

    /// <summary>Says why a YAML buffer was refused, where, and which file the edit is kept in.</summary>
    private static CliFailure EditKeptAfterRefusal(FormatException refusal, string buffer) =>
        new(
            $"The YAML in the editing buffer could not be read as a JSON document, so nothing was written. {refusal.Message} "
            + $"The edit is kept at {buffer}, readable by you alone: copy what you need from it before editing again, and remove its directory once you have.",
            refusal);

    /// <summary>Reports whether a YAML buffer that did change still asks for nothing once it is read as JSON.</summary>
    /// <remarks>
    /// A buffer holding only comments is an emptied one, and a buffer whose comments or layout alone changed describes
    /// the document the deployment already holds, which a commit would only give a new version.
    /// </remarks>
    private static bool AbandonedAfterConversion(CliContext context, string describedAs, string document, string? committed)
    {
        if (committed is null)
        {
            context.Console.WriteLine($"The buffer holds no document, so {describedAs} was left as it was.");

            return true;
        }

        if (YamlDocumentView.DescribeTheSameDocument(document, committed))
        {
            context.Console.WriteLine("The buffer describes the document already held, so nothing was written.");

            return true;
        }

        return false;
    }

    /// <summary>Opens the session's own directory and writes the document into it, readable by their user alone.</summary>
    /// <exception cref="CliFailure">Thrown when the temporary directory cannot be written, which is a situation rather than a defect.</exception>
    /// <remarks>
    /// A temporary directory that is full, read-only, or on a filesystem that will not take a user-only mode is
    /// something the operator can act on, so it is reported as a sentence naming the path rather than left to reach
    /// <c>CliRunner</c> as a stack trace — the same answer the credential store and the token protector give for the
    /// same situation.
    /// </remarks>
    private static void Open(string session, string buffer, string document)
    {
        try
        {
            UserOnlyStorage.CreateDirectory(session);

            SettingsBuffer.Write(buffer, document);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The editing buffer at {buffer} could not be written.", failure);
        }
    }

    /// <summary>Reads back what the editor saved.</summary>
    /// <exception cref="CliFailure">Thrown when the buffer cannot be read, which the editor rather than this command left it as.</exception>
    private static async Task<string> ReadBackAsync(string buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(buffer, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure(
                $"The editing buffer at {buffer} could not be read back after the editor exited, so nothing was written.",
                failure);
        }
    }

    /// <summary>Removes the session's directory and the buffer in it, letting the invocation's own outcome stand where it cannot be removed.</summary>
    /// <remarks>
    /// <para>
    /// The case is the one the commands' own guidance anticipates: a graphical editor started without its wait flag
    /// returns while still holding the file open, so the delete throws on Windows over an invocation that had already
    /// decided what it did. Turning that into a stack trace would report a failure to somebody whose command worked,
    /// or replace the sentence naming why nothing was written — which is the rule <c>CliRunner.Record</c> states for a
    /// full disk.
    /// </para>
    /// <para>
    /// What is left behind on that path is the session's own directory, which is readable by its user alone whatever
    /// the editor did to the file inside it. That is why the buffer sits in a directory of its own rather than in the
    /// temporary directory itself: an editor that saves by writing a sibling and renaming it over the target creates
    /// that sibling under the process umask, so the mode set at creation does not survive the first save — and what the
    /// file then holds is a whole description of what a deployment does for somebody, plus anything the operator typed
    /// over a marker.
    /// </para>
    /// </remarks>
    private static void Discard(string session)
    {
        try
        {
            Directory.Delete(session, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reports whether the editing session asked for nothing, and says which of the two ways it did.</summary>
    /// <remarks>
    /// An emptied buffer is the conventional way to abandon an editor-driven command and is honoured as one, rather
    /// than being read as a document that states nothing at all — which is a change an operator can still make
    /// deliberately by saving an empty JSON object.
    /// </remarks>
    private static bool Abandoned(CliContext context, string describedAs, string document, string saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            context.Console.WriteLine($"The buffer was emptied, so {describedAs} was left as it was.");

            return true;
        }

        if (string.Equals(saved, document, StringComparison.Ordinal))
        {
            context.Console.WriteLine("The buffer was saved unchanged, so nothing was written.");

            return true;
        }

        return false;
    }

    /// <summary>Says why an editing session that did not finish wrote nothing, in terms the operator can act on.</summary>
    /// <remarks>
    /// The two endings need different advice. A wait flag is what repairs an editor that ran and returned before the
    /// operator had finished; it repairs nothing for an editor the operating system never started, where the value in
    /// the variable is the thing to correct and the system's own words are what name which way it is wrong.
    /// </remarks>
    private static string WhyNothingWasWritten(string editor, EditingSession ended) =>
        ended.WhyItNeverStarted is { Length: > 0 } reason
            ? $"The editor '{editor}' could not be started, so nothing was written: {reason}. Correct ${OperatorEditor.VisualVariable} or ${OperatorEditor.EditorVariable} to name a program on this machine."
            : $"The editor '{editor}' did not finish successfully, so nothing was written. A graphical editor needs the flag that makes it wait — '{OperatorEditor.VisualVariable}=\"code --wait\"', for instance — because this command reads the file back when the editor exits.";
}
