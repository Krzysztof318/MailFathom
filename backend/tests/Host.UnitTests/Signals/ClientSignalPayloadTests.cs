// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Notifications;
using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers what each of the five kinds puts on the wire, and proves that no mail travels with any of them.</summary>
/// <remarks>
/// <para>
/// One test per kind, and each asserts the payload as a whole rather than field by field, because the claim is about
/// what does <em>not</em> cross as much as about what does: a member added later that carried a subject, an address, a
/// body fragment, or an attachment name would leave all five failing rather than passing beside it.
/// </para>
/// <para>
/// Each arrangement then hands the composition the mail-shaped text a raise site has in hand where the kind has
/// anywhere to put it, and reads the rendered JSON back for it. The owner is read for in the same pass, because the
/// connection already belongs to one and writing the identifier onto every message would be a value the client has no
/// use for.
/// </para>
/// </remarks>
public sealed class ClientSignalPayloadTests
{
    private const string Subject = "Quarterly figures, revised";

    private const string Address = "someone@example.test";

    private const string BodyFragment = "the figures we discussed on Tuesday";

    private const string AttachmentName = "figures-revised.xlsx";

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    private static readonly DateTimeOffset Instant = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An arrival says how much arrived and where, and nothing about what arrived.</summary>
    [Fact]
    public void For_MailArrived_RendersTheCountAndThePlaceAndNothingElse()
    {
        // Arrange
        var signal = ClientSignal.MailArrived(Account, Inbox, newEmailCount: 4);

        // Act
        var payload = ClientSignalPayload.For(signal);

        // Assert
        AssertPayloadIs(new ClientSignalPayload("mail.arrived", "work", Inbox.Value, 4, [], null, null, null), payload);
        AssertNothingAboutMailOrTheOwnerCrossed(payload);
    }

    /// <summary>A change names the rows to re-read as identities this deployment issued, never as anything about them.</summary>
    [Fact]
    public void For_MailChanged_RendersTheNamedIdentitiesAndNothingElse()
    {
        // Arrange
        var email = StoredEmailId.Create(Guid.CreateVersion7(Instant));
        var signal = ClientSignal.MailChanged(Account, Inbox, [email]);

        // Act
        var payload = ClientSignalPayload.For(signal);

        // Assert
        AssertPayloadIs(
            new ClientSignalPayload(
                "mail.changed",
                "work",
                Inbox.Value,
                0,
                [email.Value.ToString()],
                null,
                null,
                null),
            payload);
        AssertNothingAboutMailOrTheOwnerCrossed(payload);
    }

    /// <summary>A moved folder set names the account, and the client re-reads the tree it already reads.</summary>
    [Fact]
    public void For_FoldersChanged_RendersTheAccountAndNothingElse()
    {
        // Arrange
        var signal = ClientSignal.FoldersChanged(Account);

        // Act
        var payload = ClientSignalPayload.For(signal);

        // Assert
        AssertPayloadIs(new ClientSignalPayload("folders.changed", "work", null, 0, [], null, null, null), payload);
        AssertNothingAboutMailOrTheOwnerCrossed(payload);
    }

    /// <summary>A raised notification carries the record's own two lines, and nothing else the record holds.</summary>
    [Fact]
    public void For_NotificationRaised_RendersTheRecordsOwnTwoLinesAndNothingElse()
    {
        // Arrange
        var notification = Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(Instant)),
            SyntheticMailOwner.Deployment,
            NotificationKind.Mail,
            title: "Mail arrived",
            body: "Four messages arrived in work.",
            source: "work",
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create($"work:{Subject}:{Address}:{BodyFragment}:{AttachmentName}"),
            Instant);

        var signal = ClientSignal.NotificationRaised(notification, unreadCount: 2);

        // Act
        var payload = ClientSignalPayload.For(signal);

        // Assert
        AssertPayloadIs(
            new ClientSignalPayload(
                "notification.raised",
                null,
                null,
                2,
                [],
                nameof(NotificationKind.Mail),
                "Mail arrived",
                "Four messages arrived in work."),
            payload);
        AssertNothingAboutMailOrTheOwnerCrossed(payload);
    }

    /// <summary>A finished run names the account, and the state it left it in is re-read rather than stated twice.</summary>
    [Fact]
    public void For_AccountState_RendersTheAccountAndNothingAboutItsState()
    {
        // Arrange
        var signal = ClientSignal.AccountState(Account);

        // Act
        var payload = ClientSignalPayload.For(signal);

        // Assert
        AssertPayloadIs(new ClientSignalPayload("account.state", "work", null, 0, [], null, null, null), payload);
        AssertNothingAboutMailOrTheOwnerCrossed(payload);
    }

    /// <summary>Asserts one payload against another as a whole, so a member added later is covered rather than skipped.</summary>
    /// <param name="expected">What the kind is supposed to render as.</param>
    /// <param name="actual">What it rendered as.</param>
    /// <remarks>The named identities are compared as a sequence and then set aside, because a record compares a list member by reference and would otherwise report two equal sequences as different payloads.</remarks>
    private static void AssertPayloadIs(ClientSignalPayload expected, ClientSignalPayload actual)
    {
        Assert.Equal(expected.Emails, actual.Emails);
        Assert.Equal(expected with { Emails = [] }, actual with { Emails = [] });
    }

    private static void AssertNothingAboutMailOrTheOwnerCrossed(ClientSignalPayload payload)
    {
        var rendered = JsonSerializer.Serialize(payload);

        Assert.DoesNotContain(Subject, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Address, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(BodyFragment, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AttachmentName, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            SyntheticMailOwner.Deployment.Value.ToString(),
            rendered,
            StringComparison.OrdinalIgnoreCase);
    }
}
