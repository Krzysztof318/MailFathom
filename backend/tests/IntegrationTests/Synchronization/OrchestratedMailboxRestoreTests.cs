// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>
/// Proves against a real IMAP server that a folder emptied by a drain takes its mail back, with the flags, the
/// keywords, and the arrival the held rows recorded, and that the server names where it put each copy.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is settleable against a substitute. That <c>APPEND</c> carries a flag set and a keyword set into a
/// folder is the server's answer rather than MailKit's; that it answers with <c>APPENDUID</c> is the capability the
/// whole mode rests on, because an append whose placement is never named is one no occurrence can be written for and
/// an operator has to settle by hand; and that the folder reads back holding exactly what went into it is the only
/// form of the claim that would fail if the drain and the restore disagreed about which folder they were working in.
/// </para>
/// <para>
/// The drain half is performed here rather than assumed, so the folder the restore fills is one that really was
/// emptied message by message. It is this test's own folder, named per run: the shared mailbox has other tests in it,
/// and a claim that a folder holds exactly two messages is only true of a folder nobody else writes to. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxRestoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset ArrivedAt = new(2026, 3, 4, 8, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendRestoredAsync_AFolderTheDrainEmptied_PutsItsMailBackWithTheFlagsAndKeywordsItWasHeldWith()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        var runIdentifier = Guid.NewGuid().ToString("N");
        var folderName = $"Restored{runIdentifier}";
        var readSubject = $"restored-read-{runIdentifier}";
        var unreadSubject = $"restored-unread-{runIdentifier}";

        await mailbox.CreateFolderAsync(folderName, cancellationToken);
        await mailbox.AppendAsync(folderName, readSubject, cancellationToken);
        await mailbox.AppendAsync(folderName, unreadSubject, cancellationToken);

        var held = await mailbox.ReadAsync(folderName, cancellationToken);
        var uidValidity = await mailbox.ReadUidValidityAsync(folderName, cancellationToken);
        var folder = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create($"restored{runIdentifier}"),
            RemoteFolderPath.Create(folderName));

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var placements = await services.InScopeAsync(
            async (scope, token) =>
            {
                var accountId = SyntheticMailAccount.AccountId;
                var policy = scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(accountId);

                await using var session = await scope.GetRequiredService<IMailboxWriteSessionFactory>()
                    .OpenForWritingAsync(accountId, folder, policy, token);

                Assert.True(await session.SupportsDrainAsync(token));

                await session.ExpungeDrainedAsync(
                    uidValidity,
                    [.. held.Select(email => email.Uid)],
                    token);

                Assert.Empty(await mailbox.ReadAsync(folderName, token));

                return new[]
                {
                    await session.AppendRestoredAsync(
                        MimeOf(readSubject),
                        new RestoredEmailState(
                            IsSeen: true,
                            IsAnswered: true,
                            IsFlagged: true,
                            IsDraft: false,
                            RemoteEmailKeywords.Create(["$Label1"])),
                        ArrivedAt,
                        token),
                    await session.AppendRestoredAsync(
                        MimeOf(unreadSubject),
                        new RestoredEmailState(
                            IsSeen: false,
                            IsAnswered: false,
                            IsFlagged: false,
                            IsDraft: false,
                            RemoteEmailKeywords.None),
                        ArrivedAt,
                        token),
                };
            },
            cancellationToken);

        // Assert
        Assert.All(placements, placement => Assert.True(placement.IsReported));
        Assert.All(placements, placement => Assert.Equal(uidValidity, placement.UidValidity));

        var restored = await mailbox.ReadAsync(folderName, cancellationToken);
        Assert.Equal<IReadOnlyList<ImapUid?>>(
            [.. placements.Select(placement => placement.Uid)],
            [.. restored.Select(email => (ImapUid?)email.Uid)]);

        var read = Assert.Single(restored, email => email.Subject == readSubject);
        Assert.True(read.IsSeen);
        Assert.True(read.IsAnswered);
        Assert.True(read.IsFlagged);
        Assert.Contains("$Label1", read.Keywords);

        var unread = Assert.Single(restored, email => email.Subject == unreadSubject);
        Assert.False(unread.IsSeen);
        Assert.False(unread.IsAnswered);
        Assert.False(unread.IsFlagged);
        Assert.Empty(unread.Keywords);
    }

    /// <summary>Composes the payload the content store would serve, which is what the append carries byte for byte.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string subject) => Encoding.ASCII.GetBytes(string.Create(
        CultureInfo.InvariantCulture,
        $"From: sender@mailfathom.invalid\r\nTo: recipient@mailfathom.invalid\r\nSubject: {subject}\r\n\r\nheld body\r\n"));
}
