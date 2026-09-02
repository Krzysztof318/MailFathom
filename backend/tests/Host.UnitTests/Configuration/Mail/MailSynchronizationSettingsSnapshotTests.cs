// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

public sealed class MailSynchronizationSettingsSnapshotTests
{
    private static readonly MailOwnerId Owner =
        MailOwnerId.Create(new Guid("b4306d24-b373-4101-8cc3-9f81b6b1be87"));

    /// <summary>A run already under way keeps one owner-document version while the next run observes the commit.</summary>
    [Fact]
    public void Current_OwnerDocumentChanges_PublishesANewSnapshotWithoutChangingTheCapturedOne()
    {
        // Arrange
        var original = Account("original");
        var changed = Account("changed");
        var owners = new ServedMailOwners();
        owners.Resolved([Serving(original)]);
        var boundSettings = new StubSettingsSnapshot<MailSynchronizationOptions>(new MailSynchronizationOptions());
        var settings = new MailSynchronizationSettingsSnapshot(boundSettings, owners);
        var captured = settings.Current;
        var reload = settings.GetReloadToken();

        // Act
        owners.OwnerDocumentPublished(Owner, "owner", new OwnerAccountOptions { MailAccounts = [changed] }, 2);
        var current = settings.Current;

        // Assert
        Assert.True(reload.HasChanged);
        Assert.NotSame(captured, current);
        Assert.Same(original, captured.FindConfiguredAccount(MailAccountId.Create("original")));
        Assert.Null(captured.FindConfiguredAccount(MailAccountId.Create("changed")));
        Assert.Same(changed, current.FindConfiguredAccount(MailAccountId.Create("changed")));
    }

    private static ServedMailOwner Serving(params MailSynchronizationAccountOptions[] accounts) =>
        new(Owner, "owner", MailOwnerAccountSource.OwnerDocument, accounts);

    private static MailSynchronizationAccountOptions Account(string accountId) => new() { AccountId = accountId };
}
