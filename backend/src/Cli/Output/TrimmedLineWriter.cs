// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MailFathom.Cli.Output;

/// <summary>The stream a drawing is written through, which drops the padding it leaves at the end of a line.</summary>
/// <remarks>
/// <para>
/// A listing sets every column out to the width of its widest value, so a row whose last value is shorter than that
/// arrives with spaces after it. On a screen they are invisible; in a file somebody captured, searched, or compared they
/// are a difference nobody wrote, and in a test they are an assertion carrying characters a reader cannot see. Removing
/// them once, where the bytes leave, is what keeps every drawing free of them rather than each shape avoiding them.
/// </para>
/// <para>
/// Only a line that a newline completed is trimmed. Anything still buffered when the stream is flushed is written as it
/// stands, because a partial line is a line still being built rather than one whose end has been reached.
/// </para>
/// </remarks>
internal sealed class TrimmedLineWriter : TextWriter
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The destination is the running process's standard output or standard error, or a buffer a test owns, and this writer is a decorator over it rather than its owner. Disposing it here would close the stream the rest of the command still reports on, which is the same reason StreamWriter offers a leaveOpen mode.")]
    private readonly TextWriter destination;
    private readonly StringBuilder line = new();

    /// <summary>Initializes a new instance of the <see cref="TrimmedLineWriter" /> class.</summary>
    /// <param name="destination">Where the trimmed lines are written; this writer does not own it and never closes it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The destination is left open deliberately. It is standard output or standard error of the running process, or a
    /// buffer a test owns, and closing somebody else's stream would end the command's ability to report anything.
    /// </remarks>
    internal TrimmedLineWriter(TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        this.destination = destination;
    }

    /// <inheritdoc />
    public override Encoding Encoding => this.destination.Encoding;

    /// <inheritdoc />
    /// <remarks>Every other overload of the base class funnels into this one, so the buffering happens in one place.</remarks>
    public override void Write(char value)
    {
        if (value != '\n')
        {
            this.line.Append(value);

            return;
        }

        this.destination.WriteLine(this.line.ToString().TrimEnd('\r').TrimEnd(' '));
        this.line.Clear();
    }

    /// <inheritdoc />
    public override void Flush()
    {
        if (this.line.Length > 0)
        {
            this.destination.Write(this.line.ToString());
            this.line.Clear();
        }

        this.destination.Flush();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Flush();
        }

        base.Dispose(disposing);
    }
}
