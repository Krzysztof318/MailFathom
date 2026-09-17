// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what admitting a credential as one configured administrator produces, and where from it may be admitted.</summary>
/// <remarks>
/// Every credential kind resolves to one of these, so the two answers asserted here — whose identity a request carries,
/// and whether the address it arrived from is one the administrator may act from — hold for a key, a public key, and a
/// token alike.
/// </remarks>
public sealed class AdministratorAdmissionTests
{
    private const string SchemeName = "MailFathomApiKey:admin";

    /// <summary>The identity is named by the administrator, so the session read, the rate limiter, and every log scope read the person rather than the key.</summary>
    [Fact]
    public void IdentityFor_AKeyOfAnAdministrator_IsNamedByTheAdministratorAndKeepsTheKeysName()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice-workstation");
        administrator.Name = "alice";
        administrator.Permissions.Add(MailFathomPermission.AdminRead.Name);

        var admission = AdministratorAdmission.For(administrator);

        // Act
        var identity = admission.IdentityFor(
            "alice-workstation",
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType,
            SchemeName);

        // Assert
        var caller = new ClaimsPrincipal(identity);
        Assert.True(identity.IsAuthenticated);
        Assert.Equal("alice", identity.Name);
        Assert.Equal("alice", TransportCallerIdentity.NameOf(caller));
        Assert.Equal("alice-workstation", caller.FindFirstValue(ApiKeyAuthentication.ApiKeyNameClaimType));
        Assert.Equal([MailFathomPermission.AdminRead], TransportGrant.PermissionsCarriedBy(caller));
    }

    /// <summary>Two keys of one administrator are one person, so both resolve the one admission holding its name and grant.</summary>
    [Fact]
    public void ForEach_AnAdministratorWithTwoKeys_ComposesOneAdmissionForBoth()
    {
        // Arrange
        var alice = ConfiguredAuthentication.Administrator(
            "alice",
            ConfiguredAuthentication.ApiKey("alice-2026"),
            ConfiguredAuthentication.ApiKey("alice-2027"));
        var bob = ConfiguredAuthentication.AdministratorWithApiKey("bob");
        bob.GrantTheWholeSurface();

        // Act
        var admissions = AdministratorAdmission.ForEach([alice, bob]);

        // Assert
        var byKeyName = AdministratorConfiguration.AdministratorsByApiKeyName([alice, bob]);
        Assert.Same(admissions[byKeyName["alice-2026"]], admissions[byKeyName["alice-2027"]]);
        Assert.Equal("alice", admissions[alice].Name);
        Assert.Empty(admissions[alice].Grant);
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Administration), admissions[bob].Grant);
    }

    [Fact]
    public void AdmitsSourceOf_AnUnrestrictedAdministrator_AdmitsAnyAddressAndARequestWithNone()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var admission = AdministratorAdmission.For(ConfiguredAuthentication.AdministratorWithApiKey("alice"));

        // Act, Assert
        Assert.True(admission.AdmitsSourceOf(RequestFrom("198.51.100.7"), SchemeName, LoggerFor(logs)));
        Assert.True(admission.AdmitsSourceOf(RequestFrom(null), SchemeName, LoggerFor(logs)));
        Assert.Empty(logs.Records);
    }

    /// <summary>An address inside a named network or equal to a named address is admitted, including the IPv4-mapped form a dual-stack listener reports.</summary>
    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("::ffff:10.20.30.40")]
    [InlineData("192.0.2.10")]
    [InlineData("::ffff:192.0.2.10")]
    [InlineData("2001:db8::7")]
    public void AdmitsSourceOf_AnAddressTheAdministratorMayActFrom_IsAdmitted(string source)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var admission = AdministratorAdmission.For(RestrictedAdministrator());

        // Act, Assert
        Assert.True(admission.AdmitsSourceOf(RequestFrom(source), SchemeName, LoggerFor(logs)));
        Assert.Empty(logs.Records);
    }

    /// <summary>An address outside every configured network is refused in either spelling, and recorded in its IPv4 form where it has one.</summary>
    [Theory]
    [InlineData("198.51.100.7", "198.51.100.7")]
    [InlineData("::ffff:198.51.100.7", "198.51.100.7")]
    [InlineData("192.0.2.11", "192.0.2.11")]
    [InlineData("2001:db9::7", "2001:db9::7")]
    public void AdmitsSourceOf_AnAddressOutsideEveryNetwork_IsRefusedAndRecordedWithTheAdministratorAndTheSource(
        string source,
        string recordedSource)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var admission = AdministratorAdmission.For(RestrictedAdministrator());

        // Act
        var admitted = admission.AdmitsSourceOf(RequestFrom(source), SchemeName, LoggerFor(logs));

        // Assert
        Assert.False(admitted);

        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("alice", Assert.Contains("AdministratorName", record.Properties));
        Assert.Equal(recordedSource, Assert.Contains("SourceAddress", record.Properties));
        Assert.Equal(SchemeName, Assert.Contains("AuthenticationScheme", record.Properties));
    }

    /// <summary>Nothing about a request whose address is unknown can be shown to lie inside a network, so a restriction refuses it.</summary>
    [Fact]
    public void AdmitsSourceOf_ARestrictedAdministratorOnARequestWithNoAddress_IsRefused()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var admission = AdministratorAdmission.For(RestrictedAdministrator());

        // Act
        var admitted = admission.AdmitsSourceOf(RequestFrom(null), SchemeName, LoggerFor(logs));

        // Assert
        Assert.False(admitted);
        Assert.Equal("unknown", Assert.Contains("SourceAddress", Assert.Single(logs.Records).Properties));
    }

    private static ILogger LoggerFor(RecordingLoggerProvider logs) =>
        logs.CreateLogger(nameof(AdministratorAdmissionTests));

    private static DefaultHttpContext RequestFrom(string? source)
    {
        var request = new DefaultHttpContext();
        request.Connection.RemoteIpAddress = source is null ? null : IPAddress.Parse(source);

        return request;
    }

    private static AdministratorOptions RestrictedAdministrator()
    {
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.AllowedSourceNetworks.Add("10.0.0.0/8");
        administrator.AllowedSourceNetworks.Add("192.0.2.10");
        administrator.AllowedSourceNetworks.Add("2001:db8::/32");

        return administrator;
    }
}
