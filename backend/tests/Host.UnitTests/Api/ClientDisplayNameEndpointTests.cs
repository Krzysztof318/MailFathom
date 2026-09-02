// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the two routes a person reads and corrects the name this deployment records them under. What the boundary
/// itself decides is the shape of each answer: the name with whether a write would be accepted beside it, a refusal a
/// client can draw rather than an unexplained failure, and a person this deployment no longer holds answered as every
/// other owner-scoped route answers one.
/// </summary>
public sealed class ClientDisplayNameEndpointTests
{
    [Fact]
    public async Task ReadAsync_APersonThisDeploymentHolds_HandsThemTheirNameAndWhetherTheyMayChangeIt()
    {
        // Arrange
        var names = SignedIn(MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite);
        names.Recording("Ada Lovelace");

        // Act
        var result = await ClientDisplayNameEndpoint.ReadAsync(names.Names, TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientDisplayNameResponse>>(result.Result).Value!;

        Assert.Equal("Ada Lovelace", answered.DisplayName);
        Assert.True(answered.Changeable);
    }

    /// <summary>Somebody whose mailboxes an administrator maintains still sees their own name, drawn as text.</summary>
    [Fact]
    public async Task ReadAsync_APersonWhoMayNotChangeTheirName_StillHandsThemTheName()
    {
        // Arrange
        var names = SignedIn(MailFathomPermission.MailRead);
        names.Recording("Ada Lovelace");

        // Act
        var result = await ClientDisplayNameEndpoint.ReadAsync(names.Names, TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientDisplayNameResponse>>(result.Result).Value!;

        Assert.Equal("Ada Lovelace", answered.DisplayName);
        Assert.False(answered.Changeable);
    }

    [Fact]
    public async Task ReadAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var names = SignedIn(MailFathomPermission.MailRead);

        // Act
        var result = await ClientDisplayNameEndpoint.ReadAsync(names.Names, TestContext.Current.CancellationToken);

        // Assert
        var absent = Assert.IsType<NotFound<ProblemDetails>>(result.Result);

        Assert.Equal(StatusCodes.Status404NotFound, absent.Value!.Status);
    }

    [Fact]
    public async Task ChangeAsync_APersonCorrectingTheirName_AnswersTheNameNowRecorded()
    {
        // Arrange
        var names = SignedIn(MailFathomPermission.MailAccountsWrite);
        names.Recording("Ada Lovelace");

        // Act
        var result = await ClientDisplayNameEndpoint.ChangeAsync(
            names.Names,
            new ClientDisplayNameRequest("  Ada King "),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientDisplayNameResponse>>(result.Result).Value!;

        Assert.Equal("Ada King", answered.DisplayName);
        Assert.True(answered.Changeable);
    }

    /// <summary>A refusal is something a client can draw, which is the whole difference from letting the write fail unexplained.</summary>
    [Fact]
    public async Task ChangeAsync_ANameThisDeploymentWillNotRecord_RefusesNamingWhatToCorrect()
    {
        // Arrange
        var names = SignedIn(MailFathomPermission.MailAccountsWrite);
        names.Recording("Ada Lovelace");

        // Act
        var result = await ClientDisplayNameEndpoint.ChangeAsync(
            names.Names,
            new ClientDisplayNameRequest("   "),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.NotNull(refusal.ProblemDetails.Detail);
    }

    [Fact]
    public async Task ChangeAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var names = SignedIn(MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientDisplayNameEndpoint.ChangeAsync(
            names.Names,
            new ClientDisplayNameRequest("Ada King"),
            TestContext.Current.CancellationToken);

        // Assert
        var absent = Assert.IsType<NotFound<ProblemDetails>>(result.Result);

        Assert.Equal(StatusCodes.Status404NotFound, absent.Value!.Status);
    }

    /// <summary>
    /// The strict binding, which is what stops a client sending a field this surface never published and reading the
    /// unchanged answer as the change having landed.
    /// </summary>
    [Fact]
    public void Deserialize_ABodyCarryingAKeyNothingBinds_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientDisplayNameRequest>(
            """{"displayName":"Ada King","portrait":"data:image/png;base64,AA=="}""",
            WebFormat));
    }

    [Fact]
    public void Deserialize_ABodyStatingAName_BindsIt()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientDisplayNameRequest>(
            """{"displayName":"Ada King"}""",
            WebFormat);

        // Assert
        Assert.Equal("Ada King", request!.DisplayName);
    }

    /// <summary>How the transport reads a body, so the binding these assert is the one a request actually meets.</summary>
    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    private static NameDeployment SignedIn(params MailFathomPermission[] granted) => new(granted);

    /// <summary>The use case over a substituted envelope, reached by a caller granted what a test states.</summary>
    private sealed class NameDeployment
    {
        private readonly IMailOwnerDirectory directory = Substitute.For<IMailOwnerDirectory>();

        internal NameDeployment(MailFathomPermission[] granted)
        {
            this.directory
                .ReadOwnerAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
                .Returns((MailOwnerRecord?)null);

            var provisioning = Substitute.For<IMailOwnerProvisioning>();
            provisioning
                .RelabelAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var servedOwners = new ServedMailOwners();
            servedOwners.Resolved(
                [new(SyntheticMailOwner.Deployment, "recorded", MailOwnerAccountSource.OwnerDocument, [])]);

            this.Names = new OwnDisplayName(
                AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment, granted),
                this.directory,
                provisioning,
                servedOwners);
        }

        internal OwnDisplayName Names { get; }

        /// <summary>States the name the envelope of the person these tests act for carries.</summary>
        internal void Recording(string displayName) =>
            this.directory
                .ReadOwnerAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>())
                .Returns(new MailOwnerRecord(SyntheticMailOwner.Deployment, displayName, DocumentWrittenAtRuntime: true));
    }
}
