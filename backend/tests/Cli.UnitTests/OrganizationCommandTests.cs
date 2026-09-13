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

    /// <summary>The deployment reads an explicit null as the decision to belong to no organization, so that is what leaving one sends.</summary>
    [Fact]
    public async Task SetOrganization_None_SendsANullOrganization()
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

    public void Dispose() => this.harness.Dispose();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
