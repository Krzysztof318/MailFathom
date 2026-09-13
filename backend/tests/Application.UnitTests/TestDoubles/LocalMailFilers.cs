// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Synchronization;
using MailFathom.TestSupport;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the local filer of a deployment whose accounts are all mirrored, which is every test not about a held account.</summary>
internal static class LocalMailFilers
{
    /// <summary>Builds a filer that finds no account held, so it files nothing and reads nothing past the phase.</summary>
    /// <param name="clock">The clock the filer mints with, which it never reaches.</param>
    /// <returns>The filer.</returns>
    internal static LocalMailFiler HoldingNothing(TimeProvider clock) => new(
        Substitute.For<ILocalMailFolderStore>(),
        Substitute.For<IEmailMetadataRepository>(),
        Substitute.For<IEmailContentStore>(),
        Substitute.For<IEmailMimeReader>(),
        Substitute.For<IMailFolderMappingReader>(),
        Substitute.For<IMailFolderResolutionStore>(),
        Substitute.For<IOutgoingMailFilingPolicyReader>(),
        Substitute.For<IOutgoingMailFilingStore>(),
        ClientSignalPublishers.ReachingNobody,
        clock);
}
