// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
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
/// Proves against a real IMAP server that a folder a mapping asked for comes into existence and then holds mail.
/// </summary>
/// <remarks>
/// This is where a real <c>CREATE</c> and the account's single write connection meet, and neither can be established
/// against a substitute: a unit test proves the adapter issued the command and read the answer, while only a server can
/// show that the folder it created is one a message can afterwards be filed into. The second creation in the same test
/// is the idempotence claim, which is likewise the server's answer rather than the adapter's belief — an IMAP
/// <c>CREATE</c> against an existing folder is an error, and reading it as success is only correct if the folder really
/// is there.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedRemoteFolderCreationTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    private static readonly MailFolderAlias CreatedAlias = MailFolderAlias.Create("createdarchive");

    [Fact]
    public async Task CreateFolderAsync_FolderTheServerDoesNotHold_CreatesItAndThenAcceptsAMessageFiledIntoIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Named per run, so this test needs no folder to have been cleaned up by a previous one and collides with
        // nothing another test in the shared mailbox is watching.
        var folderName = $"CreatedArchive{Guid.NewGuid():N}";

        var subject = $"file-into-created-{Guid.NewGuid():N}";
        await mailbox.DeliverAsync(subject, cancellationToken);
        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var delivered = Assert.Single(inbox, email => email.Subject == subject);
        var occurrence = EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            Inbox.Id,
            await mailbox.ReadUidValidityAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            delivered.Uid);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var createdPaths = await services.InScopeAsync(
            async (scope, token) =>
            {
                var account = SyntheticMailAccount.AccountId;
                var policy = scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account);
                var creator = scope.GetRequiredService<IRemoteFolderCreator>();
                var configuredPath = RemoteFolderPath.Create(folderName);

                var firstPath = await creator.CreateFolderAsync(account, CreatedAlias, configuredPath, policy, token);
                var secondPath = await creator.CreateFolderAsync(account, CreatedAlias, configuredPath, policy, token);

                await using var session = await scope.GetRequiredService<IMailboxWriteSessionFactory>()
                    .OpenForWritingAsync(account, Inbox, policy, token);
                await session.RelocateAsync(occurrence, firstPath, new InMemoryMailboxMutationJournal(), token);

                return (First: firstPath, Second: secondPath);
            },
            cancellationToken);

        // Assert
        Assert.Equal(folderName, createdPaths.First.Value);
        Assert.Equal(createdPaths.First, createdPaths.Second);

        var createdFolder = await mailbox.ReadAsync(folderName, cancellationToken);
        var filedEmail = Assert.Single(createdFolder, email => email.Subject == subject);

        Assert.False(filedEmail.IsSeen);
        Assert.DoesNotContain(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            email => email.Subject == subject);
    }
}
