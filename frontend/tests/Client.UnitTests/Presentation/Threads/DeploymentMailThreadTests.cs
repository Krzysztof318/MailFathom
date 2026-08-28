// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Threads;

/// <summary>The conversation over one deployment: what opens it, what it reads, and what one gesture costs.</summary>
public sealed class DeploymentMailThreadTests
{
    /// <summary>How long a test waits on a request it is holding open before it gives up rather than hanging the run.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>One message's body, answered for the one gesture that reads one.</summary>
    private const string WholeMessage =
        """
        {
          "storedEmailId": "00000000-0000-0000-0000-000000000001",
          "availability": "Readable",
          "plainText": { "text": "What this one added, and everything it quoted.",
                         "originalCharacterCount": 45, "truncation": "None" },
          "document": null,
          "remoteImagesRequested": false
        }
        """;

    private const string MessageDetail =
        """
        {
          "storedEmailId": "00000000-0000-0000-0000-000000000001",
          "account": "personal",
          "folder": "INBOX",
          "threadId": "10000000-0000-0000-0000-000000000000",
          "sizeOctets": 1024,
          "headers": {
            "subject": "Quarterly review",
            "sentAt": "2026-08-27T09:14:00+00:00",
            "receivedAt": "2026-08-27T09:14:06+00:00",
            "participants": [
              { "role": "From", "address": "someone@example.test", "displayName": "Someone" }
            ],
            "messageId": "one@example.test",
            "inReplyTo": null,
            "references": []
          },
          "body": { "availability": "Readable", "plainText": true, "html": false },
          "sender": { "authorAuthentication": "Authenticated", "deploymentTrust": "Unknown" },
          "attachments": [
            {
              "position": 0,
              "fileName": "quarterly-review.pdf",
              "wasFileNameNormalized": false,
              "mediaType": "application/pdf",
              "sizeOctets": 3
            }
          ],
          "carried": null,
          "unread": false,
          "flagged": false,
          "answered": false
        }
        """;

    /// <summary>Nothing is read until a conversation is opened, because a screen nothing is open in is not a request.</summary>
    [Fact]
    public async Task Messages_NothingOpened_AsksTheDeploymentForNothing()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        // Act
        var messages = await over.Thread.Messages;

        // Assert
        Assert.Equal(0, messages?.Count ?? 0);
        Assert.Empty(over.Harness.Deployment.Requests);
    }

    /// <summary>Opening a conversation reads it from its beginning, in one request for the whole exchange.</summary>
    [Fact]
    public async Task OpenAsync_AConversation_ReadsItFromItsBeginningInOneRequest()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(3)),
            TestContext.Current.CancellationToken);

        // Act
        await over.Thread.OpenAsync(MailThreads.Identity, null, TestContext.Current.CancellationToken);
        await over.Until(async () => (await over.Thread.Messages)?.Count is 3);

        // Assert
        var asked = Assert.Single(over.Harness.Deployment.Requests).RequestUri;
        Assert.Equal($"/api/client/threads/{MailThreads.Identity:D}", asked.AbsolutePath);
        Assert.Contains($"pageSize={DeploymentMailThread.PageSize}", asked.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor=", asked.Query, StringComparison.Ordinal);
    }

    /// <summary>The header is drawn from what the deployment said about the whole conversation.</summary>
    [Fact]
    public async Task Reading_AConversationOpened_DrawsTheHeaderTheDeploymentAnswered()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        // Act
        await over.Thread.OpenAsync(MailThreads.Identity, null, TestContext.Current.CancellationToken);
        await over.Until(async () => (await over.Thread.Reading)?.IsOpen is true);

        // Assert
        var header = await over.Thread.Reading;
        Assert.Equal("2 messages", header!.MessageCount);
        Assert.Equal(["Someone"], header.Participants.Select(person => person.Author));
    }

    /// <summary>Selecting one message in the list is how a conversation is reached in the mail space.</summary>
    [Fact]
    public async Task Chosen_OneMessageSelectedInTheList_OpensTheConversationItIsIn()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        // Act
        await over.List.Chosen.UpdateAsync(
            _ => ImmutableList.Create(Row(1)),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Thread.Messages)?.Count is 2);

        // Assert
        var asked = Assert.Single(over.Harness.Deployment.Requests).RequestUri;
        Assert.Equal($"/api/client/threads/{MailThreads.Identity:D}", asked.AbsolutePath);
    }

    /// <summary>
    /// Several messages selected name no conversation, because a question asked about four messages is not an exchange
    /// to read.
    /// </summary>
    [Fact]
    public async Task Chosen_SeveralMessagesSelected_LeavesTheScreenWithNothingOpen()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        await over.List.Chosen.UpdateAsync(
            _ => ImmutableList.Create(Row(1)),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Thread.Messages)?.Count is 2);

        // Act
        await over.List.Chosen.UpdateAsync(
            _ => ImmutableList.Create(Row(1), Row(2)),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Thread.Reading)?.IsClosed is true);

        // Assert
        Assert.Equal(0, (await over.Thread.Messages)?.Count ?? 0);
    }

    /// <summary>A message nothing has placed in a conversation names none, which is an ordinary state of stored mail.</summary>
    [Fact]
    public async Task Chosen_AMessageInNoConversation_AsksTheDeploymentForNothing()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        // Act
        await over.List.Chosen.UpdateAsync(
            _ => ImmutableList.Create(Unthreaded(1)),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Thread.Reading)?.IsClosed is true);

        // Assert
        Assert.Empty(over.Harness.Deployment.Requests);
    }

    /// <summary>What each message added arrived with the conversation, so opening one costs no request.</summary>
    [Fact]
    public async Task ToggleAsync_AMessageOfTheConversation_ShowsWhatItAddedWithoutAskingTheDeployment()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(3)),
            TestContext.Current.CancellationToken);
        await over.Opened();

        // Act
        await over.Thread.ToggleAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);

        // Assert
        var messages = await over.Thread.Messages;
        Assert.True(messages![0].IsExpanded);
        Assert.Single(over.Harness.Deployment.Requests);
    }

    /// <summary>Collapsing a message drops the whole of it, so nothing an expansion asked for outlives it.</summary>
    [Fact]
    public async Task ToggleAsync_AnOpenMessageWhoseWholeWasRead_DropsTheWholeMessageWithTheExpansion()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(Answering(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        await over.Opened();
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(2), TestContext.Current.CancellationToken);

        Assert.True((await over.Thread.Messages)![1].ShowsWholeMessage);

        // Act
        await over.Thread.ToggleAsync(MailMessages.Key(2), TestContext.Current.CancellationToken);
        await over.Thread.ToggleAsync(MailMessages.Key(2), TestContext.Current.CancellationToken);

        // Assert
        var messages = await over.Thread.Messages;
        Assert.True(messages![1].IsExpanded);
        Assert.False(messages[1].ShowsWholeMessage);
        Assert.True(messages[1].OffersWholeMessage);
    }

    /// <summary>The whole message is the one gesture that costs a request, and it is made for one message alone.</summary>
    [Fact]
    public async Task ShowWholeMessageAsync_AMessageSomebodyAskedFor_ReadsThatMessageAndNoOther()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(Answering(MailThreads.Document(3)),
            TestContext.Current.CancellationToken);
        await over.Opened();

        // Act
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);

        // Assert
        var messages = await over.Thread.Messages;
        Assert.True(messages![0].ShowsWholeMessage);
        Assert.Equal("Quarterly review", messages[0].Message!.Subject);
        Assert.False(messages[1].ShowsWholeMessage);
        Assert.False(messages[2].ShowsWholeMessage);

        Assert.Equal(
            $"/api/client/messages/{MailMessages.Identity(1):D}/body",
            over.Harness.Deployment.Requests[^1].RequestUri.AbsolutePath);
        Assert.Contains(
            over.Harness.Deployment.Requests,
            request => request.RequestUri.AbsolutePath
                == $"/api/client/messages/{MailMessages.Identity(1):D}");
    }

    /// <summary>Asking for remote pictures is a second read of that one message, in the terms the reader allowed.</summary>
    [Fact]
    public async Task ShowRemoteContentAsync_AMessageTheReaderAllowed_ReadsThatMessageAgainWithThemAsked()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(Answering(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);

        await over.Opened();
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);

        // Act
        await over.Thread.ShowRemoteContentAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);

        // Assert
        var asked = over.Harness.Deployment.Requests[^1].RequestUri;
        Assert.Equal($"/api/client/messages/{MailMessages.Identity(1):D}/body", asked.AbsolutePath);
        Assert.Contains("remoteImages=true", asked.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAttachmentAsync_OneFileSomebodySelected_StreamsOnlyThatFileAndMarksItDownloaded()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(Answering(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);
        await over.Opened();
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);
        var request = Assert.Single((await over.Thread.Messages)![0].Message!.Attachments).Request;

        // Act
        await over.Thread.SaveAttachmentAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 2, 3], over.Saver.Saved);
        Assert.True(Assert.Single((await over.Thread.Messages)![0].Message!.Attachments).Downloaded);
        Assert.Equal(
            $"/api/client/messages/{MailMessages.Identity(1):D}/attachments/0",
            over.Harness.Deployment.Requests[^1].RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task CancelAttachment_AFileBeingSaved_CancelsItWithoutReportingAFailure()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(Answering(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);
        await over.Opened();
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);
        var request = Assert.Single((await over.Thread.Messages)![0].Message!.Attachments).Request;
        over.Saver.Hold = true;

        var saving = over.Thread.SaveAttachmentAsync(request, TestContext.Current.CancellationToken).AsTask();
        Assert.True(over.Saver.Started.Wait(Patience, TestContext.Current.CancellationToken));

        // Act
        over.Thread.CancelAttachment(request);
        await saving;

        // Assert
        var attachment = Assert.Single((await over.Thread.Messages)![0].Message!.Attachments);
        Assert.True(attachment.CanDownload);
        Assert.False(attachment.DownloadFailed);
    }

    [Fact]
    public async Task SaveAttachmentAsync_APlatformFailure_MarksTheAttachmentAsFailed()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(Answering(MailThreads.Document(2)),
            TestContext.Current.CancellationToken);
        await over.Opened();
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);
        var request = Assert.Single((await over.Thread.Messages)![0].Message!.Attachments).Request;
        over.Saver.Failure = new InvalidOperationException("The platform save surface failed.");

        // Act
        await over.Thread.SaveAttachmentAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.Single((await over.Thread.Messages)![0].Message!.Attachments).DownloadFailed);
    }

    /// <summary>
    /// A whole message that did not arrive is said on that message, because the conversation and what the message
    /// added are still on the screen and still true.
    /// </summary>
    [Fact]
    public async Task ShowWholeMessageAsync_AReadThatFailed_SaysSoOnTheMessageAndLeavesTheConversationDrawn()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/body", StringComparison.Ordinal) => Answer("{}", HttpStatusCode.BadGateway),
            var path when path.Contains("/messages/", StringComparison.Ordinal) => Answer(MessageDetail),
            _ => Answer(MailThreads.Document(2)),
        },
            TestContext.Current.CancellationToken);

        await over.Opened();

        // Act
        await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), TestContext.Current.CancellationToken);

        // Assert
        var messages = await over.Thread.Messages;
        Assert.Equal(2, messages!.Count);
        Assert.True(messages[0].ShowsWholeMessageFailure);
        Assert.False(messages[0].AwaitsWholeMessage);

        // The ask lives on the notice rather than beside it, so the button that offers the whole message is gone.
        Assert.False(messages[0].OffersWholeMessage);
    }

    /// <summary>
    /// A message closed while its whole was on its way keeps nothing of what arrived, because closing one is what
    /// drops the whole of it and an answer to a withdrawn question may not reopen it.
    /// </summary>
    [Fact]
    public async Task ShowWholeMessageAsync_AMessageClosedWhileTheReadWasOnItsWay_KeepsNothingOfWhatArrived()
    {
        // Arrange
        var cancellation = TestContext.Current.CancellationToken;

        using var reached = new ManualResetEventSlim(false);
        using var released = new ManualResetEventSlim(false);

        using var over = await ThreadOver.CreateAsync(request =>
        {
            if (IsThread(request))
            {
                return Answer(MailThreads.Document(2));
            }

            if (IsMessage(request))
            {
                return Answer(MessageDetail);
            }

            reached.Set();
            released.Wait(Patience, cancellation);

            return Answer(WholeMessage);
        },
            TestContext.Current.CancellationToken);

        await over.Opened();

        // The read is started on a thread of its own because the scripted deployment holds the request open on the one
        // that made it, which is how a test states that a message's whole is still in flight.
        var reading = Task.Run(
            async () => await over.Thread.ShowWholeMessageAsync(MailMessages.Key(1), cancellation),
            cancellation);

        Assert.True(reached.Wait(Patience, cancellation));

        // Act
        await over.Thread.ToggleAsync(MailMessages.Key(1), cancellation);

        released.Set();
        await reading;

        // Assert
        var messages = await over.Thread.Messages;
        Assert.False(messages![0].IsExpanded);
        Assert.False(messages[0].ShowsWholeMessage);

        await over.Thread.ToggleAsync(MailMessages.Key(1), cancellation);

        messages = await over.Thread.Messages;
        Assert.False(messages![0].ShowsWholeMessage);
        Assert.True(messages[0].OffersWholeMessage);
    }

    /// <summary>A conversation longer than one page continues from the cursor the last page answered with.</summary>
    [Fact]
    public async Task ShowMoreAsync_MoreOfTheConversation_TakesThePageOntoTheEndOfWhatWasRead()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(request => Answer(
            Cursor(request) is null
                ? MailThreads.Document(2, nextCursor: "after-2")
                : MailThreads.Document(2, from: 3)),
            TestContext.Current.CancellationToken);

        await over.Opened();

        // Act
        await over.Thread.ShowMoreAsync(TestContext.Current.CancellationToken);

        // Assert
        var messages = await over.Thread.Messages;
        Assert.Equal(
            [MailMessages.Key(1), MailMessages.Key(2), MailMessages.Key(3), MailMessages.Key(4)],
            messages!.Select(message => message.Key));

        Assert.False(await over.Thread.HasMoreMessages);
        Assert.Contains(
            "cursor=after-2",
            over.Harness.Deployment.Requests[^1].RequestUri.Query,
            StringComparison.Ordinal);
    }

    /// <summary>A page of the conversation that did not arrive is said beside it rather than instead of it.</summary>
    [Fact]
    public async Task ShowMoreAsync_APageThatFailed_KeepsWhatIsDrawnAndSaysThePageDidNotArrive()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(request => Cursor(request) is null
            ? Answer(MailThreads.Document(2, nextCursor: "after-2"))
            : Answer("{}", HttpStatusCode.ServiceUnavailable),
            TestContext.Current.CancellationToken);

        await over.Opened();

        // Act
        await over.Thread.ShowMoreAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, (await over.Thread.Messages)!.Count);
        Assert.True(await over.Thread.PagingFailed);
    }

    /// <summary>Asking again reaches the session as well, because a lost connection is what usually broke the read.</summary>
    [Fact]
    public async Task AskAgainAsync_AConversationThatDidNotArrive_AsksTheSessionAndTheDeploymentAgain()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(1)),
            TestContext.Current.CancellationToken);
        await over.Opened();

        // Act
        await over.Thread.AskAgainAsync(TestContext.Current.CancellationToken);
        await over.Until(() => ValueTask.FromResult(over.Harness.Deployment.Requests.Count > 1));

        // Assert
        Assert.Equal(1, over.Session.Refreshes);
    }

    /// <summary>Reopening a conversation at another message opens there rather than where the last reading did.</summary>
    [Fact]
    public async Task OpenAsync_TheSameConversationAtAnotherMessage_OpensAtTheMessageNamed()
    {
        // Arrange
        using var over = await ThreadOver.CreateAsync(_ => Answer(MailThreads.Document(3)),
            TestContext.Current.CancellationToken);

        await over.Thread.OpenAsync(
            MailThreads.Identity,
            MailMessages.Identity(1),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Thread.Messages)?[0].IsOpenedAt is true);

        // Act
        await over.Thread.OpenAsync(
            MailThreads.Identity,
            MailMessages.Identity(2),
            TestContext.Current.CancellationToken);

        await over.Until(async () => (await over.Thread.Messages)?[1].IsOpenedAt is true);

        // Assert
        var messages = await over.Thread.Messages;
        Assert.Equal([false, true, false], messages!.Select(message => message.IsExpanded));
    }

    /// <summary>Answers the conversation, a message's details, and its whole body from their own documents.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Answering(string conversation) =>
        request => request switch
        {
            _ when IsBody(request) => Answer(WholeMessage),
            _ when IsAttachment(request) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) },
            _ when IsMessage(request) => Answer(MessageDetail),
            _ => Answer(conversation),
        };

    private static HttpResponseMessage Answer(string document, HttpStatusCode status = HttpStatusCode.OK) =>
        StubTransport.JsonResponse(document, status);

    private static bool IsBody(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.EndsWith("/body", StringComparison.Ordinal) is true;

    private static bool IsMessage(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.Contains("/messages/", StringComparison.Ordinal) is true
        && !IsBody(request)
        && !IsAttachment(request);

    private static bool IsAttachment(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.Contains("/attachments/", StringComparison.Ordinal) is true;

    private static bool IsThread(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.Contains("/threads/", StringComparison.Ordinal) is true;

    /// <summary>Reads the cursor a request carried, which is what tells one page of a script apart from the next.</summary>
    private static string? Cursor(HttpRequestMessage request) =>
        (request.RequestUri?.Query ?? string.Empty)
            .TrimStart('?')
            .Split('&')
            .FirstOrDefault(stated => stated.StartsWith("cursor=", StringComparison.Ordinal))?["cursor=".Length..];

    /// <summary>A row of the message list, in the conversation every arrangement here is of.</summary>
    private static MessageRow Row(int number) => Drawn(number, MailThreads.Identity);

    /// <summary>A row of the message list that nothing has placed in a conversation.</summary>
    private static MessageRow Unthreaded(int number) => Drawn(number, null);

    private static MessageRow Drawn(int number, Guid? threadId) => new(
        MailMessages.Key(number),
        threadId,
        "Someone",
        "Quarterly review",
        "What this one added",
        "14:02",
        "Someone, Quarterly review, 14:02",
        IsUnread: false,
        IsFlagged: false,
        IsAnswered: false,
        HasAttachments: false,
        AttachmentCount: 0);

    private sealed class ThreadOver : IDisposable
    {
        private ThreadOver(DeploymentHarness harness)
        {
            this.Harness = harness;
            this.Session = new StubClientSession(
                SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", ["mailfathom.mail.read"])));

            this.List = new StubMessageList();

            this.Saver = new StubMailAttachmentSaver();
            this.Thread = new DeploymentMailThread(this.Harness.Client, this.Session, this.List, this.Saver, Words());
        }

        /// <summary>Builds the conversation over a scripted deployment the client is already pointed at.</summary>
        /// <param name="deployment">How the deployment answers.</param>
        /// <param name="cancellationToken">Abandons the pointing.</param>
        /// <returns>The arrangement the test owns.</returns>
        internal static async ValueTask<ThreadOver> CreateAsync(
            Func<HttpRequestMessage, HttpResponseMessage> deployment,
            CancellationToken cancellationToken = default) =>
            new(await DeploymentHarness.CreateAsync(deployment, cancellationToken: cancellationToken));

        internal DeploymentHarness Harness { get; }

        internal StubClientSession Session { get; }

        internal StubMessageList List { get; }

        internal StubMailAttachmentSaver Saver { get; }

        internal DeploymentMailThread Thread { get; }

        /// <summary>Opens the conversation every test but the first begins from, and waits for it to arrive.</summary>
        /// <returns>The wait.</returns>
        internal async Task Opened()
        {
            await this.Thread.OpenAsync(MailThreads.Identity, null, TestContext.Current.CancellationToken);

            await this.Until(async () => (await this.Thread.Messages)?.Count > 0);
        }

        /// <summary>Waits until a consequence of a feed re-evaluating has happened, or fails the test.</summary>
        /// <param name="settled">What is being waited for.</param>
        /// <returns>The wait.</returns>
        /// <remarks>
        /// Only what reaches the conversation through a feed rather than through a call is waited on: opening one, and
        /// the list's selection reaching it. Each of those is finished by MVUX re-evaluating a feed on a thread of its
        /// own, and no await a test holds is the one that finishes it. Everything the conversation does inside an
        /// awaited method is asserted straight after that await instead. Polling rather than a recorded feed, for the
        /// reason <c>frontend/tests/AGENTS.md</c> gives about the package that records one.
        /// </remarks>
        internal async Task Until(Func<ValueTask<bool>> settled)
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                if (await settled())
                {
                    return;
                }

                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Fail("The conversation did not settle on what the test was waiting for.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Session.Dispose();
            this.Harness.Dispose();
        }

        private static StubStringLocalizer Words() => new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageWords.NoSenderKey] = "Unknown sender",
            [MessageWords.NoSubjectKey] = "No subject",
            [MessageWords.NoDateKey] = "No date",
            [MessageWords.MoreRecipientsKey] = "{0} and {1} more",
            [MessageWords.AnnouncementKey] = "{0}, {1}, {2}",
            [MessageWords.UnreadKey] = "unread",
            [MessageWords.FlaggedKey] = "flagged",
            [MessageWords.AnsweredKey] = "answered",
            [MessageWords.AttachmentsKey] = "attachment",
            [ThreadWords.MessageCountKey] = "{0} messages",
            [ThreadWords.AnnouncementKey] = "Conversation {0}. {1}",
            [ThreadWords.RecipientsKey] = "To {0}",
            [ThreadWords.ParticipantMessagesKey] = "({0})",
            [ThreadWords.ParticipantAnnouncementKey] = "{0} wrote {1}",
            [MailMessageWords.TrustedSenderKey] = "Trusted {0}",
            [MailMessageWords.FailedSenderKey] = "Failed {0}",
            [MailMessageWords.UnrecognizedSenderKey] = "Unrecognized",
            [MailMessageWords.AttachmentFallbackKey] = "Attachment {0}",
            [MailMessageWords.NormalizedFileNameKey] = "The sender's file name was made safe.",
            [MailMessageWords.HeaderRoleKey("From")] = "From",
            [MailMessageWords.HeaderRoleKey("SentAt")] = "Sent",
            [MailMessageWords.HeaderRoleKey("ReceivedAt")] = "Received",
            [MailMessageWords.HeaderRoleKey("MessageId")] = "Message ID",
        });
    }

    private sealed class StubMailAttachmentSaver : IMailAttachmentSaver
    {
        internal ManualResetEventSlim Started { get; } = new(false);

        internal bool Hold { get; set; }

        internal Exception? Failure { get; set; }

        internal byte[] Saved { get; private set; } = [];

        public async ValueTask<bool> SaveAsync(
            DeploymentMailAttachment attachment,
            Func<Stream, CancellationToken, Task> write,
            CancellationToken cancellationToken)
        {
            this.Started.Set();

            if (this.Failure is { } failure)
            {
                throw failure;
            }

            if (this.Hold)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            await using var destination = new MemoryStream();
            await write(destination, cancellationToken);
            this.Saved = destination.ToArray();

            return true;
        }
    }
}
