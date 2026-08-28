// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail.Reading;

/// <summary>The reading pane's model: what it asks the deployment for, and what it refuses to remember between messages.</summary>
public sealed class MailBodyModelTests
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["MailBody.Notice.RemoteContent.Message"] = "References removed: {0}.",
        ["MailBody.Notice.RemoteContentShown.Message"] = "Loaded from other servers: {0}.",
    };

    private const string Message = "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1";
    private const string OtherMessage = "2c1743a3-91b2-4c39-9f2d-6a7b8c9d0e1f";

    private const string Readable =
        """
        {
          "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
          "availability": "Readable",
          "plainText": { "text": "Words", "originalCharacterCount": 5, "truncation": "None" },
          "document": {
            "schemaVersion": 1, "refusal": "None", "removedRemoteReferenceCount": 3,
            "retainedRemoteImageCount": 0, "inlineImageCount": 0, "undrawnInlineImageCount": 0,
            "truncated": false,
            "blocks": [ { "type": "paragraph", "version": 1, "alignment": "Start",
                          "content": [ { "text": "Hello", "emphasis": "None",
                                         "foreground": null, "link": null } ] } ]
          },
          "remoteImagesRequested": false
        }
        """;

    /// <summary>A pane nothing has been opened in reads nothing, so opening the space asks the deployment for no mail.</summary>
    [Fact]
    public async Task Body_NothingOpened_ReadsNothingAndAsksTheDeploymentForNothing()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        var reading = await model.Body;

        // Assert
        Assert.NotNull(reading);
        Assert.False(reading.IsOpen);
        Assert.Empty(harness.Deployment.Requests);
    }

    /// <summary>Every message is opened on the terms every message is opened on: without whatever it asks another server for.</summary>
    [Fact]
    public async Task Open_AMessage_ReadsItWithoutItsRemoteContent()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        await model.Open(Guid.Parse(Message), TestContext.Current.CancellationToken);
        var reading = await model.Body;

        // Assert
        Assert.NotNull(reading);
        Assert.True(reading.IsOpen);
        Assert.True(reading.DrawsDocument);
        Assert.True(reading.WithholdsRemoteContent);
        Assert.Equal("References removed: 3.", reading.WithheldRemoteContent);
        Assert.NotEmpty(harness.Deployment.Requests);
        Assert.All(
            harness.Deployment.Requests,
            request => Assert.DoesNotContain("remoteImages", request.RequestUri.Query, StringComparison.Ordinal));
    }

    /// <summary>The reader's answer about remote content is a second read of the same message rather than a setting.</summary>
    [Fact]
    public async Task ShowRemoteContent_AnOpenMessage_ReadsThatMessageAgainAskingForIt()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        await model.Open(Guid.Parse(Message), TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.ShowRemoteContent(TestContext.Current.CancellationToken);
        _ = await model.Body;

        // Assert
        Assert.DoesNotContain(
            "remoteImages",
            harness.Deployment.Requests[0].RequestUri.Query,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $"/api/client/messages/{Message}/body?remoteImages=true",
            harness.Deployment.Requests[^1].RequestUri.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Nothing to show remote content for is nothing to read, so the pane does not ask on an empty message.</summary>
    [Fact]
    public async Task ShowRemoteContent_NothingOpened_AsksTheDeploymentForNothing()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        await model.ShowRemoteContent(TestContext.Current.CancellationToken);
        var reading = await model.Body;

        // Assert
        Assert.NotNull(reading);
        Assert.False(reading.IsOpen);
        Assert.Empty(harness.Deployment.Requests);
    }

    /// <summary>
    /// The allowance never outlives the message it was given for. It is the same value as the message, so the next
    /// message cannot inherit it and nothing has to remember to clear it.
    /// </summary>
    [Fact]
    public async Task Open_AnotherMessageAfterRemoteContentWasShown_AsksForThatMessageWithoutIt()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        await model.Open(Guid.Parse(Message), TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.ShowRemoteContent(TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.Open(Guid.Parse(OtherMessage), TestContext.Current.CancellationToken);
        _ = await model.Body;

        // Assert
        Assert.EndsWith(
            $"/api/client/messages/{OtherMessage}/body",
            harness.Deployment.Requests[^1].RequestUri.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Opening the same message again asks again, because the previous answer was never written down.</summary>
    [Fact]
    public async Task Open_TheSameMessageAfterRemoteContentWasShown_AsksForItWithoutRemoteContentAgain()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        await model.Open(Guid.Parse(Message), TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.ShowRemoteContent(TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.Close(TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.Open(Guid.Parse(Message), TestContext.Current.CancellationToken);
        var reading = await model.Body;

        // Assert
        Assert.NotNull(reading);
        Assert.True(reading.WithholdsRemoteContent);
        Assert.EndsWith(
            $"/api/client/messages/{Message}/body",
            harness.Deployment.Requests[^1].RequestUri.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Closing leaves the pane empty rather than showing the message somebody stopped reading.</summary>
    [Fact]
    public async Task Close_AnOpenMessage_LeavesThePaneWithNothingInIt()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));
        await using var model = new MailBodyModel(harness.Client, Localizer());

        // Act
        await model.Open(Guid.Parse(Message), TestContext.Current.CancellationToken);
        _ = await model.Body;
        await model.Close(TestContext.Current.CancellationToken);
        var reading = await model.Body;

        // Assert
        Assert.NotNull(reading);
        Assert.False(reading.IsOpen);
        Assert.False(reading.DrawsDocument);
    }

    /// <summary>A pane that could be built without either service would be one that cannot read or cannot say anything.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(Readable));

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailBodyModel(null!, Localizer()));
        Assert.Throws<ArgumentNullException>(() => new MailBodyModel(harness.Client, null!));
    }

    private static StubStringLocalizer Localizer() => new(Words);
}
