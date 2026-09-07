// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>A buffer a command may open, read or write, and close, and that the test still holds afterwards.</summary>
/// <param name="buffer">What the command actually reads or writes.</param>
/// <remarks>
/// A corpus is a file, and the command owns the stream it was handed: it closes it as soon as it is done. A test
/// supplies the stream instead of the file system, so closing it would take away the thing being asserted on — and a
/// test replaying one corpus twice would find the second run reading from the end of it. Closing therefore rewinds
/// instead, which is what a second <c>File.OpenRead</c> of one path would have given.
/// </remarks>
internal sealed class UnclosedStream(MemoryStream buffer) : Stream
{
    /// <inheritdoc />
    public override bool CanRead => buffer.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => buffer.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => buffer.CanWrite;

    /// <inheritdoc />
    public override long Length => buffer.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => buffer.Position;
        set => buffer.Position = value;
    }

    /// <inheritdoc />
    public override void Flush() => buffer.Flush();

    /// <inheritdoc />
    public override int Read(byte[] target, int offset, int count) => buffer.Read(target, offset, count);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => buffer.Seek(offset, origin);

    /// <inheritdoc />
    public override void SetLength(long value) => buffer.SetLength(value);

    /// <inheritdoc />
    public override void Write(byte[] source, int offset, int count) => buffer.Write(source, offset, count);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            buffer.Position = 0;
        }

        base.Dispose(disposing);
    }
}
