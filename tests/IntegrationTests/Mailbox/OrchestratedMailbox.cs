// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AppHost;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.SyntheticMail.Generation;
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

    /// <summary>What every message this suite composes is generated from, so two runs seed the mailbox identically.</summary>
    private const int SyntheticSeed = 602;

    private static readonly MailboxAddress MailboxRecipient = new(
        "MailFathom integration mailbox",
        OrchestrationContract.MailServerAccountEmailAddress);

    private static readonly MailboxAddress SyntheticSender = new(
        "MailFathom integration sender",
        "sender@mailfathom.test");

    /// <summary>Delivers one synthetic message, which the server files unread.</summary>
    /// <param name="subject">The subject, which is how a test recognizes its own message among the mailbox's.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <remarks>
    /// The envelope is stated rather than read out of the headers. A generated message names invented participants
    /// under a reserved domain, and letting MailKit derive the envelope from those would ask the server to deliver to
    /// each of them — which is neither what this suite is arranging nor something the container should be asked to do.
    /// </remarks>
    internal async Task DeliverAsync(string subject, CancellationToken cancellationToken)
    {
        using var message = CreateSyntheticMessage(subject);
        using var client = new SmtpClient();

        await client.ConnectAsync(endpoints.SmtpHost, endpoints.SmtpPort, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, SyntheticSender, [MailboxRecipient], cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>Appends one synthetic message directly to a folder, for folders SMTP delivery does not reach.</summary>
    /// <param name="folderPath">The folder to append to.</param>
    /// <param name="subject">The subject the appended message carries.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    internal async Task AppendAsync(string folderPath, string subject, CancellationToken cancellationToken)
    {
        using var message = CreateSyntheticMessage(subject);

        await this.AppendAsync(folderPath, message, MessageFlags.None, cancellationToken);
    }

    /// <summary>Appends one synthetic message as a draft, the way the mailbox owner's own mail client saves one.</summary>
    /// <param name="folderPath">The folder to append to, which is the one playing the drafts role.</param>
    /// <param name="subject">The subject the appended message carries.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <remarks>
    /// This is the draft MailFathom did not write, and it is the control every claim about a draft's own copy is read
    /// against: it sits in the same folder, carries the same flag, and is reached by the same commands, so a removal
    /// that took one message too many is visible here rather than nowhere.
    /// </remarks>
    internal async Task AppendDraftAsync(string folderPath, string subject, CancellationToken cancellationToken)
    {
        using var message = CreateSyntheticMessage(subject);

        await this.AppendAsync(folderPath, message, MessageFlags.Draft, cancellationToken);
    }

    /// <summary>Appends one synthetic message written by a stated author, carrying headers the generator writes none of.</summary>
    /// <param name="folderPath">The folder to append to.</param>
    /// <param name="subject">The subject the appended message carries, which also names its identifier.</param>
    /// <param name="author">Who the message is from, which is what a test about correspondents has to arrange.</param>
    /// <param name="additionalHeaders">Headers written onto the composed message, empty where it needs none.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <remarks>
    /// The identifier is derived from the subject rather than left at whatever the seed produced, because the generator
    /// is asked for one message and every append would otherwise carry the same <c>Message-Id</c> — which anything
    /// counting distinct messages reads as one message stored repeatedly rather than as several.
    /// </remarks>
    internal async Task AppendAsync(
        string folderPath,
        string subject,
        SyntheticParticipant author,
        IReadOnlyList<(string Name, string Value)> additionalHeaders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(additionalHeaders);

        using var message = CreateSyntheticMessage(subject, author);

        foreach (var (name, value) in additionalHeaders)
        {
            message.Headers.Add(name, value);
        }

        await this.AppendAsync(folderPath, message, MessageFlags.None, cancellationToken);
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
                    summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                    summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                    summary.Flags?.HasFlag(MessageFlags.Draft) == true,
                    summary.Keywords is { } keywords ? [.. keywords] : [])),
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

    /// <summary>Moves one message between folders over a connection MailFathom knows nothing about.</summary>
    /// <param name="sourceFolderPath">The folder holding the message.</param>
    /// <param name="destinationFolderPath">The folder to move it into.</param>
    /// <param name="uid">The message to move.</param>
    /// <param name="cancellationToken">Cancels the move.</param>
    /// <returns>The UID the destination folder reports for the message, where the server named one.</returns>
    /// <remarks>
    /// This is the mailbox owner doing by hand exactly what a rule would ask MailFathom to do, and it is the control the
    /// suppression is only meaningful against: the two produce the same two events in the same two folders, and the only
    /// thing that separates them is whether MailFathom wrote a record before the command went out. A suppression that
    /// silenced this one would silence the owner's own mailbox.
    /// </remarks>
    internal async Task<ImapUid?> MoveAsync(
        string sourceFolderPath,
        string destinationFolderPath,
        ImapUid uid,
        CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var source = await client.GetFolderAsync(sourceFolderPath, cancellationToken);
        var destination = await client.GetFolderAsync(destinationFolderPath, cancellationToken);
        await source.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        var placed = await source.MoveToAsync(new UniqueId(uid.Value), destination, cancellationToken);

        await source.CloseAsync(expunge: false, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return placed is { } placement ? ImapUid.Create(placement.Id) : null;
    }

    /// <summary>Flags one message <c>\Deleted</c> without expunging it, the way another mail client would.</summary>
    /// <param name="folderPath">The folder holding the message.</param>
    /// <param name="uid">The message to flag.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <remarks>
    /// This is the neighbour a message-scoped expunge has to leave alone. A bare IMAP <c>EXPUNGE</c> removes every
    /// message in the folder carrying this flag, so a message flagged here and still present afterwards is the whole
    /// proof that MailFathom issued <c>UID EXPUNGE</c> rather than the unscoped command — and it has to be flagged over
    /// a connection the adapter knows nothing about, because the point is that MailFathom never saw it.
    /// </remarks>
    internal async Task MarkDeletedAsync(string folderPath, ImapUid uid, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var folder = await client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        await folder.AddFlagsAsync(new UniqueId(uid.Value), MessageFlags.Deleted, silent: true, cancellationToken);

        // expunge: false is the point of the method. Closing with an expunge would remove the message this just
        // flagged, and the test would then pass without MailFathom having been asked to spare anything.
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

    /// <summary>Puts an empty folder under a name the server does not hold, without selecting it.</summary>
    /// <param name="folderName">The folder to create beneath the personal namespace root.</param>
    /// <param name="cancellationToken">Cancels the connection and the command.</param>
    /// <returns>A task that completes once the server holds the folder.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the server accepts the creation without answering with the folder.</exception>
    /// <remarks>
    /// The folder is deliberately never opened, which is what separates this from <see cref="RecreateFolderAsync" />
    /// and what makes it the one creation a later <see cref="DeleteFolderAsync" /> may be paired with: the remarks on
    /// that method record why a selected folder cannot be deleted here. Nothing is retired out of the way either, so a
    /// name the server already holds fails the creation rather than being silently replaced.
    /// </remarks>
    internal async Task CreateFolderAsync(string folderName, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var personalNamespace = await client.GetFolderAsync(client.PersonalNamespaces[0].Path, cancellationToken);

        _ = await personalNamespace.CreateAsync(folderName, isMessageFolder: true, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The mail server accepted CREATE for '{folderName}' without returning the folder it created.");

        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>Removes one folder from the mailbox, so a test can model a destination somebody deleted.</summary>
    /// <param name="folderName">The folder to remove; it must exist, because a test that removed nothing proves nothing.</param>
    /// <param name="cancellationToken">Cancels the connection and the command.</param>
    /// <returns>A task that completes once the server no longer holds the folder.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the mailbox holds no folder of that name.</exception>
    /// <remarks>
    /// Only a folder no session has selected may be removed this way. GreenMail 2.1.11 drops the connection when it
    /// deletes a folder an earlier session selected, for the reason <see cref="RecreateFolderAsync" /> records at
    /// length — which is why that method retires a folder by renaming it and why pairing it with this one reaches the
    /// defect rather than avoiding it. <see cref="CreateFolderAsync" /> is the creation this is paired with.
    /// </remarks>
    internal async Task DeleteFolderAsync(string folderName, CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var personalNamespace = await client.GetFolderAsync(client.PersonalNamespaces[0].Path, cancellationToken);
        var existingFolders = await personalNamespace.GetSubfoldersAsync(subscribedOnly: false, cancellationToken);
        var folderToRemove = existingFolders.FirstOrDefault(
            folder => StringComparer.Ordinal.Equals(folder.Name, folderName))
            ?? throw new InvalidOperationException(
                $"The mail server holds no folder named '{folderName}', so nothing was removed.");

        await folderToRemove.DeleteAsync(cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static Task DelayPastTheNextSecondAsync(CancellationToken cancellationToken)
    {
        var millisecondsIntoTheCurrentSecond = TimeProvider.System.GetUtcNow().Millisecond;

        return Task.Delay(
            TimeSpan.FromMilliseconds(1_000 - millisecondsIntoTheCurrentSecond + 50),
            TimeProvider.System,
            cancellationToken);
    }

    /// <summary>Builds one message through the repository's own synthetic-mail generator.</summary>
    /// <remarks>
    /// <para>
    /// The generator lives in <c>tools/SyntheticMail</c> and is what a developer fills a development mailbox with, so
    /// this suite composes through it rather than keeping a second builder of its own: two implementations of "build a
    /// synthetic message" would drift, and the drift would be between the mail this suite proves things about and the
    /// mail anybody actually works against.
    /// </para>
    /// <para>
    /// One fixed seed and a count of one, so the message is the same on every run; only the subject is replaced,
    /// because the subject is how a test recognizes its own message among the mailbox's. Attachments are switched off
    /// because nothing here asserts about one and every byte of one would cross the container boundary per delivery.
    /// Fabricated sensitive material is switched off for a stronger reason than that: every test in this suite reads
    /// mail through a deployment that scans nothing, so a planted credential would be a paragraph of noise in every
    /// assertion about a body — and a suite that did switch a scanner on would want to say which decoy it planted
    /// rather than inherit a share of them.
    /// </para>
    /// </remarks>
    private static MimeMessage CreateSyntheticMessage(string subject, SyntheticParticipant? author = null)
    {
        var plan = new SyntheticCorpusPlan(
            SyntheticSeed,
            Count: 1,
            LatestSentAt: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            SpanDays: 1,
            MaximumAttachmentBytes: 0,
            SensitivePercentage: 0);

        var seeded = SyntheticEmailGenerator.Generate(plan)[0];
        var generated = author is null
            ? seeded with { Subject = subject }
            : seeded with { Subject = subject, Author = author, MessageId = $"{subject}@mailfathom.test" };

        return SyntheticMimeComposer.Compose(
            generated,
            MailboxRecipient,
            SyntheticSender,
            SyntheticAuthorIdentity.Fabricated);
    }

    /// <summary>Appends a composed message to a folder, in the unread state a delivered one arrives in.</summary>
    /// <remarks>
    /// The flags are the caller's because <c>\Draft</c> is what separates a message somebody is still writing from one
    /// that arrived; the unread state is not negotiable and the append below says why.
    /// </remarks>
    private async Task AppendAsync(
        string folderPath,
        MimeMessage message,
        MessageFlags flags,
        CancellationToken cancellationToken)
    {
        using var client = await this.ConnectAndAuthenticateAsync(cancellationToken);

        var folder = await client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        // Never MessageFlags.Seen. The invariant under test is about mail the server considers unread, so an appended
        // message must arrive in the same state a delivered one does.
        await folder.AppendAsync(message, flags, cancellationToken);
        await folder.CloseAsync(expunge: false, cancellationToken);

        await client.DisconnectAsync(quit: true, cancellationToken);
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
