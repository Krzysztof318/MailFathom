// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about how much each configured credential and administrator may do.</summary>
/// <remarks>
/// A grant nobody wrote down reaches the whole of its surface, and this report is the only place a deployment running
/// on that default can read it. What matters most here is that an entry which never narrowed says so in its own line
/// rather than being reported as though somebody had chosen the permissions it holds.
/// </remarks>
public sealed class TransportGrantStartupReportTests
{
    [Fact]
    public async Task StartAsync_WithNeitherEndpointEnabled_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions(), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>There is no entry for a grant to hang on, so a caller admitted here holds everything the surface publishes.</summary>
    [Fact]
    public async Task StartAsync_AnEnabledEndpointWithNoEntry_SaysEveryCallerHoldsTheWholeSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions { Enabled = true }, new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("MCP", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/mcp", Assert.Contains("EndpointPath", record.Properties));
        Assert.Equal(
            WholeMailSurface,
            Assert.Contains("GrantedPermissions", record.Properties));
        Assert.Equal("McpEndpoint:Authentication", Assert.Contains("AuthenticationSettingPath", record.Properties));
    }

    /// <summary>
    /// The line an operator meets on a first run for a mail-serving endpoint: they wrote which method is accepted, and
    /// what each credential of it may do is a fact about the credential rather than about the entry — so the line says
    /// where to read it instead of stating a grant the entry does not carry.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserFacingEntry_NamesTheMethodAndWhereItsGrantIsRecorded()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            McpEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("McpEndpoint:Authentication:0", Assert.Contains("EntrySettingPath", record.Properties));
        Assert.Equal("api-key", Assert.Contains("AcceptedMethod", record.Properties));
        Assert.Contains("mfctl credential list", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("GrantedPermissions", record.Properties.Select(property => property.Key));
    }

    /// <summary>
    /// A token narrows what its own credential grants rather than what the entry states, so the line has to say the
    /// recorded permissions are a ceiling. Reading the two lines the same way would over-read every token's grant.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserFacingEntryNarrowedByTokenScopes_SaysTheRecordedGrantIsACeiling()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var entry = ConfiguredAuthentication.AcceptingSubjectsFrom("https://mail.example.test/mcp");
        entry.PermissionsFromTokenScopes = true;

        var report = ReportFor(McpEndpointWith(entry), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains("its own scopes carry", record.Message, StringComparison.Ordinal);
        Assert.Equal("oauth-subject", Assert.Contains("AcceptedMethod", record.Properties));
    }

    /// <summary>
    /// The line is what an operator goes and edits, so it names the key they wrote rather than the position the binder
    /// appended the entry at. A source numbering its entries with a gap makes the two different numbers, and the
    /// position then names a path their configuration does not contain.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnEntryWrittenUnderAKeyOfItsOwn_NamesThatKeyRatherThanTheBoundPosition()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var entry = Accepting(UserCredentialMethod.ApiKey);
        entry.RecordConfigurationKey("2");

        var report = ReportFor(McpEndpointWith(entry), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("McpEndpoint:Authentication:2", Assert.Contains("EntrySettingPath", record.Properties));
    }

    /// <summary>The startup record is where an operator meets the posture first, so a line stating a grant without saying whether it bites is the false answer the report exists to avoid.</summary>
    [Fact]
    public async Task StartAsync_AnEntryOnTheMailSurface_SaysTheGrantDecidesWhatIsServed()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();

        var report = ReportFor(
            McpEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains(
            "served only the tools its grant permits",
            Assert.Contains("GrantEnforcement", record.Properties)?.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>The two surfaces refuse differently, so a line that said the same of both would be wrong about one of them.</summary>
    [Fact]
    public async Task StartAsync_AnEntryOnTheAdministrativeSurface_SaysARefusalNamesThePermissionThatWouldSuffice()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            new McpEndpointOptions { Enabled = false },
            AdminEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains(
            "refused with that permission named",
            Assert.Contains("GrantEnforcement", record.Properties)?.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>The client surface configures its credentials in a section of its own, so its permissive posture is read there rather than inferred from the MCP endpoint's.</summary>
    [Fact]
    public async Task StartAsync_AnEnabledClientEndpointWithNoEntry_SaysEveryCallerHoldsTheWholeMailSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            new McpEndpointOptions(),
            new AdminEndpointOptions(),
            logs,
            new ClientEndpointOptions { Enabled = true });

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("client", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/api/client", Assert.Contains("EndpointPath", record.Properties));
        Assert.Equal(
            WholeMailSurface,
            Assert.Contains("GrantedPermissions", record.Properties));
        Assert.Equal("ClientEndpoint:Authentication", Assert.Contains("AuthenticationSettingPath", record.Properties));
    }

    /// <summary>Every enabled endpoint is reported separately, so a deployment serving all three reads three lines rather than one surface standing in for another.</summary>
    [Fact]
    public async Task StartAsync_AllThreeEndpointsEnabled_ReportsEachEntryAgainstItsOwnSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var clientEndpoint = new ClientEndpointOptions { Enabled = true };
        clientEndpoint.Authentication.Add(Accepting(UserCredentialMethod.Password));

        var report = ReportFor(
            McpEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            AdminEndpointWith(Accepting(UserCredentialMethod.Password)),
            logs,
            clientEndpoint);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["McpEndpoint:Authentication:0", "AdminEndpoint:Authentication:0", "ClientEndpoint:Authentication:0"],
            logs.Records.Select(record => Assert.Contains("EntrySettingPath", record.Properties)));
        Assert.Equal(
            ["api-key", "password", "password"],
            logs.Records.Select(record => Assert.Contains("AcceptedMethod", record.Properties)));
    }

    /// <summary>The mail and administrative surfaces refuse differently, so an operator has to be able to read back that they narrowed the one they meant.</summary>
    [Fact]
    public async Task StartAsync_BothEndpointsEnabled_ReportsEachEntryAgainstItsOwnSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            McpEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            AdminEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.Equal("MCP", Assert.Contains("EndpointName", logs.Records.ElementAt(0).Properties));
        Assert.Contains(
            "served only the tools its grant permits",
            Assert.Contains("GrantEnforcement", logs.Records.ElementAt(0).Properties)?.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "refused with that permission named",
            Assert.Contains("GrantEnforcement", logs.Records.ElementAt(1).Properties)?.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>An entry here grants nothing of its own, so the line says the grant is the user's roles and that a user holding none is refused.</summary>
    [Fact]
    public async Task StartAsync_AnAdministrativeEntry_SaysEachCallerHoldsWhatTheirUsersAdministrativeRolesGrant()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions(), AdminEndpointWith(Accepting(UserCredentialMethod.ApiKey)), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("AdminEndpoint:Authentication:0", Assert.Contains("EntrySettingPath", record.Properties));
        Assert.Equal("api-key", Assert.Contains("AcceptedMethod", record.Properties));
        Assert.Contains("administrative roles grant", record.Message, StringComparison.Ordinal);
        Assert.Contains("holding none is refused", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_AnAdministrativeEntryNarrowedByTokenScopes_SaysTheTokensScopesKeepPartOfTheRoles()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var entry = ConfiguredAuthentication.AcceptingSubjectsFrom("https://mail.example.test/api/admin");
        entry.PermissionsFromTokenScopes = true;

        var report = ReportFor(new McpEndpointOptions(), AdminEndpointWith(entry), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains("kept to those its own scopes carry", record.Message, StringComparison.Ordinal);
    }

    /// <summary>An enabled administrative endpoint accepting no method serves its callers as the default administrator, and the line says where a method would be added.</summary>
    [Fact]
    public async Task StartAsync_AnEnabledAdministrativeEndpointWithNoEntry_SaysEveryCallerActsAsTheDefaultAdministrator()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions(), new AdminEndpointOptions { Enabled = true }, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("AdminEndpoint:Authentication", Assert.Contains("AuthenticationSettingPath", record.Properties));
        Assert.Contains("default administrator 'admin'", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_AfterStarting_SaysNothingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            McpEndpointWith(Accepting(UserCredentialMethod.ApiKey)),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    /// <summary>Every permission the MCP surface publishes, written as the report writes a grant.</summary>
    /// <remarks>
    /// Composed rather than spelled out, because what these tests pin is that an unnarrowed entry resolves to the whole
    /// of its surface — not which capabilities that surface happens to publish today. Spelling it out would fail every
    /// one of them the next time a permission is allocated, which is a fact about the vocabulary and belongs to the test
    /// that pins the vocabulary.
    /// </remarks>
    private static string WholeMailSurface => string.Join(
        ", ",
        MailFathomPermission.PublishedFor(ProtectedSurface.Mail).Select(permission => permission.Name));

    private static UserFacingAuthenticationOptions Accepting(UserCredentialMethod method) =>
        ConfiguredAuthentication.Accepting(method);

    private static McpEndpointOptions McpEndpointWith(UserFacingAuthenticationOptions entry)
    {
        var endpointSettings = new McpEndpointOptions { Enabled = true };
        endpointSettings.Authentication.Add(entry);

        return endpointSettings;
    }

    private static AdminEndpointOptions AdminEndpointWith(UserFacingAuthenticationOptions entry)
    {
        var endpointSettings = new AdminEndpointOptions { Enabled = true };
        endpointSettings.Authentication.Add(entry);

        return endpointSettings;
    }

    private static TransportGrantStartupReport ReportFor(
        McpEndpointOptions mcpEndpointSettings,
        AdminEndpointOptions adminEndpointSettings,
        RecordingLoggerProvider logs,
        ClientEndpointOptions? clientEndpointSettings = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new TransportGrantStartupReport(
            Options.Create(mcpEndpointSettings),
            Options.Create(adminEndpointSettings),
            Options.Create(clientEndpointSettings ?? new ClientEndpointOptions()),
            loggerFactory.CreateLogger<TransportGrantStartupReport>());
    }
}
