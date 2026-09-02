// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Describes one file staged against a draft, without the octets it is made of.</summary>
/// <remarks>
/// <para>
/// A file is uploaded once and composed into every later revision of the draft, which is what keeps editing a message
/// with a large attachment from re-sending that attachment on each keystroke. So it is held beside the draft rather
/// than inside the composed message, and the message the drafts folder shows is the composition of the fields and
/// these.
/// </para>
/// <para>
/// The description travels apart from the content for the reason every other mail description does: listing a draft
/// says what is attached to it, and a listing that carried the octets would load every file of every draft to answer
/// what they are called.
/// </para>
/// <para>
/// <see cref="MediaType" /> is the author's statement about their own file rather than anything derived from it, on the
/// same terms as the composed attachment: what a wrong one costs is a recipient's client opening the file with the
/// wrong application, which is the author's mistake to make and to correct.
/// </para>
/// </remarks>
/// <param name="Id">What a removal names the file by.</param>
/// <param name="FileName">The name the recipient will see the file under.</param>
/// <param name="MediaType">The media type the author declared the file as.</param>
/// <param name="ByteLength">How many octets were stored for it.</param>
/// <param name="StagedAt">When the upload was taken in, which is the order a composition attaches the files in.</param>
public sealed record MailDraftAttachment(
    MailDraftAttachmentId Id,
    string FileName,
    string MediaType,
    long ByteLength,
    DateTimeOffset StagedAt)
{
    /// <summary>The greatest length a staged file's name may carry.</summary>
    /// <remarks>
    /// A bound on untrusted input rather than a property of files: the name is the author's and ends up in a header,
    /// so an unbounded one is a way to write a paragraph into a column and then into a message. What refuses a name a
    /// header cannot carry at all is the composition, which is where every authored field is judged.
    /// </remarks>
    public const int MaximumFileNameLength = 256;

    /// <summary>The greatest length a staged file's declared media type may carry.</summary>
    /// <remarks>RFC 6838 bounds a type and a subtype at 127 characters each, so this holds any registered name with its parameters.</remarks>
    public const int MaximumMediaTypeLength = 255;
}
