// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Synchronization;
using NSubstitute;

namespace MailFathom.TestSupport;

/// <summary>Builds the copier a change submission commits a held copy through.</summary>
/// <remarks>
/// The copier is a concrete type rather than a port, so a test that is not about copying builds a real one over
/// substituted stores instead of substituting the copier itself. Left to its defaults the content store finds no
/// payload for any message, so a copy submitted through it is refused and every other change behaves exactly as it
/// did — which is what the many call sites constructing a submission to exercise a flag or a move need.
/// </remarks>
public static class LocalMailCopiers
{
    /// <summary>Builds a copier over the stores a test supplies, substituting the ones it does not.</summary>
    /// <param name="folders">The folder store the copy is filed through, which is the submission's own where a test copies.</param>
    /// <param name="contents">Reads the copied payload and places the copy's.</param>
    /// <param name="emails">Writes the copy's stored message.</param>
    /// <param name="mimeReader">Reads the metadata the copy is searched and threaded by.</param>
    /// <returns>The copier.</returns>
    public static LocalMailCopier Over(
        ILocalMailFolderStore? folders = null,
        IEmailContentStore? contents = null,
        IEmailMetadataRepository? emails = null,
        IEmailMimeReader? mimeReader = null) =>
        new(
            emails ?? Substitute.For<IEmailMetadataRepository>(),
            contents ?? Substitute.For<IEmailContentStore>(),
            mimeReader ?? Substitute.For<IEmailMimeReader>(),
            folders ?? Substitute.For<ILocalMailFolderStore>());
}
