// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the commands that record and list organizations, and the one that moves a user between them.</summary>
/// <remarks>
/// What these hold is the part of each act that lives in the command: what a listing puts under which heading, what a
/// write sends, and that leaving every organization is a stated decision rather than an option nobody wrote.
/// </remarks>
public sealed class OrganizationCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private static readonly Guid User = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Organization = new("77777777-7777-7777-7777-777777777777");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The short name is the half of a login an operator reads the listing for, so it is drawn under its own heading beside the identifier every other command takes.</summary>
    [Fact]
    public async Task List_ADeploymentHoldingAnOrganization_DrawsEachValueUnderItsHeading()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding(
            [User],
            FakeOrganizationDeployment.Organization(Organization, "ACME", "Acme Corporation", members: 2));

        // Act
        var exitCode = await this.RunAsync(deployment, "organization", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(
            this.harness.Console.Lines, "Organization", "Short name", "Display name", "Members", "Recorded");
        var row = Assert.Single(listing.Rows);

        Assert.Equal($"{Organization:D}", listing.Cell(row, "Organization"));
        Assert.Equal("ACME", listing.Cell(row, "Short name"));
        Assert.Equal("Acme Corporation", listing.Cell(row, "Display name"));
        Assert.Equal("2", listing.Cell(row, "Members"));
    }

    /// <summary>The identifier is the deployment's to mint, so the command sends both names and reports what came back.</summary>
    [Fact]
    public async Task Add_ADisplayNameAndAShortName_SendsBothAndReportsTheIdentifierItMinted()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding([User]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "add",
            "--display-name",
            "Acme Corporation",
            "--short-name",
            "ACME",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var recording = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.OrganizationsPath));
        using var body = JsonDocument.Parse(recording.ContentAsUtf8String());

        Assert.Equal("Acme Corporation", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("ACME", body.RootElement.GetProperty("shortName").GetString());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"{FakeOrganizationDeployment.ProvisionedOrganizationId:D}", StringComparison.Ordinal));
    }

    /// <summary>The deployment refuses a body stating no decision, so leaving every organization is sent as one rather than as a missing identifier.</summary>
    [Fact]
    public async Task SetOrganization_None_SendsTheDecisionToBelongToNone()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding([User]);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "set-organization", "--none", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var move = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.UserOrganizationPath(User)));
        using var body = JsonDocument.Parse(move.ContentAsUtf8String());

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("organizationId").ValueKind);
        Assert.True(body.RootElement.GetProperty("none").GetBoolean());
    }

    /// <summary>An organization named beside the decision to belong to none contradicts itself, so it is answered rather than resolved to either.</summary>
    [Fact]
    public async Task SetOrganization_AnOrganizationAndNoneAtOnce_IsRefusedWithoutMovingAnybody()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding([User]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "user",
            "set-organization",
            "--organization",
            $"{Organization:D}",
            "--none",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.UserOrganizationPath(User)));
    }

    /// <summary>Renaming reaches the display name alone, so a rename can never move the half of every member's login the short name is.</summary>
    [Fact]
    public async Task Rename_ADisplayName_SendsItToTheDisplayNameRouteAndNowhereElse()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding([User]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "rename",
            "--organization",
            $"{Organization:D}",
            "--display-name",
            "Acme Holdings",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var rename = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.OrganizationDisplayNamePath(Organization)));
        using var body = JsonDocument.Parse(rename.ContentAsUtf8String());

        Assert.Equal("Acme Holdings", body.RootElement.GetProperty("displayName").GetString());
        Assert.Empty(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.OrganizationShortNamePath(Organization)));
    }

    /// <summary>A short name moves every member's login, so it is sent to its own route and never to the display name's.</summary>
    [Fact]
    public async Task SetShortName_AShortName_SendsItToTheShortNameRouteAndNowhereElse()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding([User]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "set-short-name",
            "--organization",
            $"{Organization:D}",
            "--short-name",
            "ACMEH",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var change = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.OrganizationShortNamePath(Organization)));
        using var body = JsonDocument.Parse(change.ContentAsUtf8String());

        Assert.Equal("ACMEH", body.RootElement.GetProperty("shortName").GetString());
        Assert.Empty(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.OrganizationDisplayNamePath(Organization)));
    }

    [Fact]
    public async Task Remove_AnOrganization_DeletesThatOrganizationAndWritesNothingElse()
    {
        // Arrange
        using var deployment = FakeOrganizationDeployment.Holding([User]);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "remove",
            "--organization",
            $"{Organization:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.OrganizationPath(Organization)));
        Assert.DoesNotContain(deployment.RecordedRequests, request => request.Method == HttpMethod.Put);
    }

    public void Dispose() => this.harness.Dispose();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
