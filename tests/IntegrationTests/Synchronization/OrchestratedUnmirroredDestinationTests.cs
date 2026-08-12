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
/// Proves against a real IMAP server that a folder the account maps and never mirrors is resolved when a change first
/// needs it, and then takes both a relocation and a copy over the account's existing write session.
/// </summary>
/// <remarks>
/// Neither half can be established against a substitute. That an alias resolves on demand is a claim about what a
/// server advertises and about a binding surviving in the database; that the folder then holds mail is the server's
/// answer to a <c>MOVE</c> and a <c>COPY</c> issued into a folder no synchronization run has ever opened. The two are
/// one test because the second is only meaningful against the folder the first resolved.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedUnmirroredDestinationTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    [Fact]
    public async Task ResolveAsync_ADestinationTheAccountOnlyMaps_BindsItOnDemandAndThenTakesAMoveAndACopy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Named per run, so nothing has to have been cleaned up by a previous one and no other test in the shared
        // mailbox is watching the folder this one files into.
        var runIdentifier = Guid.NewGuid().ToString("N");
        var folderName = $"UnmirroredJunk{runIdentifier}";
        var alias = MailFolderAlias.Create($"unmirrored{runIdentifier}");

        var relocatedSubject = $"relocate-into-unmirrored-{runIdentifier}";
        var copiedSubject = $"copy-into-unmirrored-{runIdentifier}";
        await mailbox.DeliverAsync(relocatedSubject, cancellationToken);
        await mailbox.DeliverAsync(copiedSubject, cancellationToken);

        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var uidValidity = await mailbox.ReadUidValidityAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var relocated = OccurrenceOf(inbox, relocatedSubject, uidValidity);
        var copied = OccurrenceOf(inbox, copiedSubject, uidValidity);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var binding = await services.InScopeAsync(
            async (scope, token) =>
            {
                var account = SyntheticMailAccount.AccountId;
                var policy = scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account);
                var configuredPath = RemoteFolderPath.Create(folderName);

                await scope.GetRequiredService<IRemoteFolderCreator>()
                    .CreateFolderAsync(account, alias, configuredPath, policy, token);

                var mapping = MailFolderMapping.ToRemotePath(
                    alias,
                    configuredPath,
                    MailFolderParticipation.MappedOnly);
                var resolution = await scope.GetRequiredService<MailFolderResolver>()
                    .ResolveAsync(account, mapping, policy, token);

                var destinationPath = resolution.Resolution!.RemotePath;

                await using var session = await scope.GetRequiredService<IMailboxWriteSessionFactory>()
                    .OpenForWritingAsync(account, Inbox, policy, token);
                await session.RelocateAsync(relocated, destinationPath, new InMemoryMailboxMutationJournal(), token);
                await session.CopyAsync(copied, destinationPath, new InMemoryMailboxMutationJournal(), token);

                return await scope.GetRequiredService<IMailFolderResolutionStore>()
                    .GetCurrentResolutionAsync(account, alias, token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(folderName, binding!.RemotePath.Value);

        var destinationFolder = await mailbox.ReadAsync(folderName, cancellationToken);
        var filedSubjects = destinationFolder.Select(email => email.Subject).ToArray();
        Assert.Contains(relocatedSubject, filedSubjects);
        Assert.Contains(copiedSubject, filedSubjects);
        Assert.All(destinationFolder, email => Assert.False(email.IsSeen));

        var remainingInbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var remainingSubjects = remainingInbox.Select(email => email.Subject).ToArray();
        Assert.DoesNotContain(relocatedSubject, remainingSubjects);
        Assert.Contains(copiedSubject, remainingSubjects);
    }

    private static EmailOccurrenceId OccurrenceOf(
        IReadOnlyList<ObservedEmail> inbox,
        string subject,
        ImapUidValidity uidValidity) => EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            Inbox.Id,
            uidValidity,
            Assert.Single(inbox, email => email.Subject == subject).Uid);
}
