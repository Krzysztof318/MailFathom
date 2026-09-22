// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the change submission a use case writes through.</summary>
/// <remarks>
/// Left to its defaults, a substituted local folder store answers no holding for any account, which is the phase every
/// account had before custody existed, so every change submitted through it is written down as a record exactly as it was
/// before. A test about a held account hands in the stores that say otherwise.
/// </remarks>
internal static class MailboxChangeSubmissions
{
    internal static MailboxChangeSubmission Over(
        IMailboxMutationRecordStore records,
        ILocalMailFolderStore? localFolders = null,
        ILocalEmailStateStore? states = null,
        LocalMailCopier? copier = null,
        IMailboxMutationAuditEntryStore? auditEntries = null,
        IMailboxMutationAuditSettingsReader? auditSettings = null,
        ClientSignals? signals = null,
        TimeProvider? timeProvider = null)
    {
        var settings = auditSettings;

        if (settings is null)
        {
            settings = Substitute.For<IMailboxMutationAuditSettingsReader>();
            settings.GetAuditSettings(Arg.Any<MailAccountId>()).Returns(MailboxMutationAuditSettings.Disabled);
        }

        var folders = localFolders ?? Substitute.For<ILocalMailFolderStore>();

        return new MailboxChangeSubmission(
            folders,
            records,
            states ?? Substitute.For<ILocalEmailStateStore>(),
            copier ?? LocalMailCopiers.Over(folders),
            settings,
            auditEntries ?? Substitute.For<IMailboxMutationAuditEntryStore>(),
            signals ?? ClientSignalPublishers.ReachingNobody,
            timeProvider ?? new FakeTimeProvider());
    }
}
