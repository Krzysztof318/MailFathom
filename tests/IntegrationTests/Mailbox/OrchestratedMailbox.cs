// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AppHost;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MailFathom.IntegrationTests.Mailbox;

/// <summary>Seeds the orchestrated mailbox and observes it over connections the code under test knows nothing about.</summary>
/// <remarks>
/// <para>
/// This is the suite's independent witness. Reading flag state through the adapter under test would only report what
/// that adapter believes, so every observation here is made over its own IMAP connection, and every one of them uses a
/// command incapable of setting a flag: <c>UID FETCH (FLAGS)</c> reports flags and changes none.
/// </para>
/// <para>
/// A connection per operation, rather than one held open for the fixture's lifetime. The suite makes a handful of these
/// calls per test, so the cost is irrelevant beside the container it is talking to, and no connection state can leak
/// from one observation into the next or into the session a test is asserting about.
/// </para>
/// </remarks>
internal sealed class OrchestratedMailbox(OrchestratedMailServerEndpoints endpoints)
{
    /// <summary>The path of the folder mail delivered over SMTP arrives in.</summary>
    internal const string InboxPath = "INBOX";

    /// <summary>Delivers one synthetic message, which the server files unread.</summary>
    /// <param name="subject">The subject, which is how a test recognizes its own message among the mailbox's.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    internal async Task DeliverAsync(string subject, CancellationToken cancellationToken)
    {
        using var message = CreateSyntheticMessage(subject);
        using var client = new SmtpClient();

        await client.ConnectAsync(endpoints.SmtpHost, endpoints.SmtpPort, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>Appends one synthetic message directly to a folder, for folders SMTP delivery does not reach.</summary>
    /// <param name="folderPath">The folder to append to.</param>
    /// <param name="subject">The subject the appended message carries.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    internal async Task AppendAsync(string folderPath, string subject, CancellationToken cancellationToken)
    {
        using var message = CreateSyntheticMessage(subject);
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var folder = await client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        // MessageFlags.None on purpose. The invariant under test is about mail the server considers unread, so an
        // appended message must arrive in the same state a delivered one does.
        await folder.AppendAsync(message, MessageFlags.None, cancellationToken);
        await folder.CloseAsync(expunge: false, cancellationToken);

        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>Reads every message in a folder with the flags the server currently holds for it.</summary>
    /// <param name="folderPath">The folder to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per message, in the server's UID order.</returns>
    internal async Task<IReadOnlyList<ObservedEmail>> ReadAsync(string folderPath, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var folder = await client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var summaries = await folder.FetchAsync(
            0,
            -1,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | MessageSummaryItems.Envelope,
            cancellationToken);

        await folder.CloseAsync(expunge: false, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return
        [
            .. summaries
                .OrderBy(summary => summary.UniqueId.Id)
                .Select(summary => new ObservedEmail(
                    ImapUid.Create(summary.UniqueId.Id),
                    summary.Envelope?.Subject,
                    summary.Flags?.HasFlag(MessageFlags.Seen) == true)),
        ];
    }

    /// <summary>Reads the UIDVALIDITY a folder currently reports.</summary>
    /// <param name="folderPath">The folder to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value the server answers a selection with.</returns>
    internal async Task<ImapUidValidity> ReadUidValidityAsync(string folderPath, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var folder = await client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uidValidity = ImapUidValidity.Create(folder.UidValidity);

        await folder.CloseAsync(expunge: false, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return uidValidity;
    }

    /// <summary>Sets the remote Seen flag on one message, which is the control the invariant assertions rest on.</summary>
    /// <param name="folderPath">The folder holding the message.</param>
    /// <param name="uid">The message to mark.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <remarks>
    /// An assertion that a flag is unset proves nothing unless the same observation would report it set, and a server
    /// that simply never records the flag would satisfy every one of those assertions. Marking a message here and
    /// reading it back is what rules that out. It is a <c>STORE</c> rather than a non-<c>PEEK</c> fetch because MailKit
    /// publishes no way to issue one — every retrieval it offers is already <c>BODY.PEEK</c> — and what the control has
    /// to establish is that the observation channel can see a set flag at all.
    /// </remarks>
    internal async Task MarkSeenAsync(string folderPath, ImapUid uid, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var folder = await client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        await folder.AddFlagsAsync(new UniqueId(uid.Value), MessageFlags.Seen, silent: true, cancellationToken);

        await folder.CloseAsync(expunge: false, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>Puts a brand-new folder under a name, retiring whatever the server currently holds there.</summary>
    /// <param name="folderName">The folder to create beneath the personal namespace root.</param>
    /// <param name="cancellationToken">Cancels the recreation.</param>
    /// <returns>The UIDVALIDITY the server assigned to the new folder.</returns>
    /// <remarks>
    /// <para>
    /// This is how a test produces a UIDVALIDITY change without simulating one: a folder the server has never seen
    /// before gets a value of its own, so every UID handed out under the previous incarnation now names nothing.
    /// </para>
    /// <para>
    /// The old folder is renamed out of the way rather than deleted, because GreenMail 2.1.11 crashes on <c>DELETE</c>
    /// of a folder that an earlier session had selected: it notifies the folder's registered listeners and dereferences
    /// a response object that a disconnected session no longer has, which drops the connection with
    /// <c>IllegalStateException: Can not handle IMAP connection</c>. Every folder this suite deletes has been selected
    /// by a synchronization run, so that path is unreachable here. Renaming reaches the same end state, leaves the
    /// retired folder in a container that is discarded with the run, and is unaffected by the defect.
    /// </para>
    /// <para>
    /// The wait is not a guess. GreenMail derives a folder's UIDVALIDITY from <c>System.currentTimeMillis() / 1000</c>,
    /// so a folder recreated inside the same wall-clock second is handed back the value it just had, and the change the
    /// test asserts on would be absent for a reason that has nothing to do with MailFathom. Crossing the next second
    /// boundary first makes the new value certain rather than likely.
    /// </para>
    /// </remarks>
    internal async Task<ImapUidValidity> RecreateFolderAsync(string folderName, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var personalNamespace = await client.GetFolderAsync(client.PersonalNamespaces[0].Path, cancellationToken);
        var existingFolders = await personalNamespace.GetSubfoldersAsync(subscribedOnly: false, cancellationToken);
        var existingFolder = existingFolders.FirstOrDefault(
            folder => StringComparer.Ordinal.Equals(folder.Name, folderName));

        if (existingFolder is not null)
        {
            await existingFolder.RenameAsync(
                personalNamespace,
                $"{folderName}Retired{Guid.NewGuid():N}",
                cancellationToken);
            await DelayPastTheNextSecondAsync(cancellationToken);
        }

        var recreatedFolder = await personalNamespace.CreateAsync(folderName, isMessageFolder: true, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The mail server accepted CREATE for '{folderName}' without returning the folder it created.");

        await recreatedFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uidValidity = ImapUidValidity.Create(recreatedFolder.UidValidity);

        await recreatedFolder.CloseAsync(expunge: false, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return uidValidity;
    }

    private static Task DelayPastTheNextSecondAsync(CancellationToken cancellationToken)
    {
        var millisecondsIntoTheCurrentSecond = TimeProvider.System.GetUtcNow().Millisecond;

        return Task.Delay(
            TimeSpan.FromMilliseconds(1_000 - millisecondsIntoTheCurrentSecond + 50),
            TimeProvider.System,
            cancellationToken);
    }

    private static MimeMessage CreateSyntheticMessage(string subject)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain") { Text = $"Synthetic body of {subject}." },
        };

        message.From.Add(new MailboxAddress("MailFathom integration sender", "sender@mailfathom.test"));
        message.To.Add(new MailboxAddress(
            "MailFathom integration mailbox",
            OrchestrationContract.MailServerAccountEmailAddress));

        return message;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the connected client passes to the caller, which disposes it when its observation ends.")]
    private async Task<ImapClient> ConnectAndAuthenticateAsync(CancellationToken cancellationToken)
    {
        var client = new ImapClient();
        try
        {
            await client.ConnectAsync(endpoints.ImapHost, endpoints.ImapPort, SecureSocketOptions.None, cancellationToken);

            // The server advertises a SASL mechanism this suite does not use, and MailKit would otherwise negotiate it.
            // Emptying the set selects the IMAP LOGIN command, which is the same choice the adapter under test makes.
            client.AuthenticationMechanisms.Clear();
            await client.AuthenticateAsync(
                OrchestrationContract.MailServerAccountUserName,
                OrchestrationContract.MailServerAccountPassword,
                cancellationToken);

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }
}
