// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>One attachment held open long enough to be described and then written out.</summary>
/// <remarks>
/// <para>
/// Two steps rather than one, because a download states what it is before it says how much of it there is: the media
/// type, the file name, and the length are response headers, and they have to be written before the first octet of the
/// body. A single call returning bytes would have forced the whole file into memory to learn its size.
/// </para>
/// <para>
/// The instance owns the parse behind it and is disposed by whoever opened it. Nothing here is buffered: the octets are
/// decoded straight into the destination the caller supplies, so a large attachment costs the copy buffer rather than
/// its own size.
/// </para>
/// </remarks>
public interface IOpenedEmailAttachment : IAsyncDisposable
{
    /// <summary>Gets what the attachment is, measured by the same parse that will write it.</summary>
    ExtractedEmailAttachment Description { get; }

    /// <summary>Writes the attachment's decoded octets, and nothing else, to the destination.</summary>
    /// <param name="destination">Where the octets are written.</param>
    /// <param name="cancellationToken">Cancels the copy when the reader disconnects.</param>
    /// <returns>A task that completes once the whole attachment has been written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination" /> is <see langword="null" />.</exception>
    Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken);
}
