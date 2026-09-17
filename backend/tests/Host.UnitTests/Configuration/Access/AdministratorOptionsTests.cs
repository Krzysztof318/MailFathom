// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers which arrangements of one administrator the administrative endpoint accepts, and what an accepted one holds.</summary>
/// <remarks>
/// An administrator is the unit a grant and a network restriction are written on, so the shapes worth stating are the
/// ones an operator could write believing they had named somebody, narrowed them, or confined them — and each refusal
/// names the setting under the administrator it belongs to.
/// </remarks>
public sealed class AdministratorOptionsTests
{
    private const string SettingPath = "AdminEndpoint:Administrators:0";

    [Fact]
    public void FindConfigurationErrors_ANamedAdministratorWithACredential_ReportsNothing()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");

        // Act, Assert
        Assert.Empty(administrator.FindConfigurationErrors(SettingPath));
    }

    /// <summary>Every act is attributed to the name, so an administrator without one could only be recorded as nobody.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_AnAdministratorWithoutAName_IsRefusedAtTheName(string? name)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Name = name;

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith($"{SettingPath}:Name — every administrator needs a name", reported, StringComparison.Ordinal);
    }

    /// <summary>A name reaches log lines verbatim, so a line break in it would let a configuration forge a record.</summary>
    [Fact]
    public void FindConfigurationErrors_ANameCarryingAControlCharacter_IsRefused()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Name = "alice\nbob";

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Name", reported, StringComparison.Ordinal);
        Assert.Contains("control character", reported, StringComparison.Ordinal);
    }

    /// <summary>The name an unauthenticated caller carries cannot also be an administrator's, or no record could tell the two apart.</summary>
    [Theory]
    [InlineData("anonymous")]
    [InlineData(" Anonymous ")]
    public void FindConfigurationErrors_TheNameAnUnauthenticatedCallerCarries_IsRefused(string name)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Name = name;

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Name", reported, StringComparison.Ordinal);
        Assert.Contains("'anonymous'", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatedName_ANamePaddedWithWhitespace_IsReportedTrimmed()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Name = "  alice ";

        // Act, Assert
        Assert.Equal("alice", administrator.ValidatedName());
    }

    /// <summary>An administrator nobody can present a credential for is a name that could never act, which is a misspelled block rather than a posture.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAdministratorStatingNoCredential_IsRefusedAtTheCredentials()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator("alice");

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith($"{SettingPath}:Credentials — this administrator states no credential", reported, StringComparison.Ordinal);
    }

    /// <summary>A credential's own refusal is named under the administrator and the credential's position, which is the line an operator edits.</summary>
    [Fact]
    public void FindConfigurationErrors_ACredentialStatingNoMethod_IsNamedUnderTheAdministrator()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            ConfiguredAuthentication.ApiKey("alice"),
            new AdministratorCredentialOptions());

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith($"{SettingPath}:Credentials:1 — this entry states no credential", reported, StringComparison.Ordinal);
    }

    /// <summary>Several credentials of one administrator are how a key is rotated, so they are an ordinary arrangement rather than a conflict.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAdministratorHoldingSeveralCredentials_ReportsNothing()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            ConfiguredAuthentication.ApiKey("alice-2026"),
            ConfiguredAuthentication.ApiKey("alice-2027"),
            ConfiguredAuthentication.PublicKey("alice-laptop"));

        // Act, Assert
        Assert.Empty(administrator.FindConfigurationErrors(SettingPath));
    }

    /// <summary>
    /// The pair this whole reading exists for. An absent key and an emptied list arrive from the binder identically
    /// and mean opposite things: one reaches the whole surface, the other reaches nothing and is how an administrator is
    /// suspended without being deleted.
    /// </summary>
    [Fact]
    public void GrantedPermissions_AnAdministratorThatWroteNoGrant_ReachesTheWholeSurface()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.GrantTheWholeSurface();

        // Act
        var granted = administrator.GrantedPermissions();

        // Assert
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Administration), granted);
    }

    [Fact]
    public void GrantedPermissions_AnAdministratorThatWroteAnEmptyGrant_ReachesNothing()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");

        // Act, Assert
        Assert.Empty(administrator.GrantedPermissions());
        Assert.Empty(administrator.FindConfigurationErrors(SettingPath));
    }

    /// <summary>A grant is a set, so the order it resolves in is the published one rather than the one it was typed in.</summary>
    [Fact]
    public void GrantedPermissions_AGrantWrittenOutOfOrder_ResolvesInThePublishedOrder()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add(MailFathomPermission.AdminOperate.Name);
        administrator.Permissions.Add(MailFathomPermission.AdminRead.Name);

        // Act
        var granted = administrator.GrantedPermissions();

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead, MailFathomPermission.AdminOperate], granted);
    }

    [Fact]
    public void GrantedPermissions_AGrantNamingASubtree_ReachesEveryPermissionBeneathIt()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add("mailfathom.admin.audit.*");

        // Act, Assert
        Assert.Equal([MailFathomPermission.AdminAuditRead], administrator.GrantedPermissions());
    }

    /// <summary>The administrative endpoint enforces one half of the vocabulary, so a pattern spanning both grants that half and never the other.</summary>
    [Fact]
    public void GrantedPermissions_AWildcardSpanningBothSurfaces_GrantsOnlyTheAdministrativeHalf()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add("mailfathom.*.read");

        // Act
        var granted = administrator.GrantedPermissions();

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead, MailFathomPermission.AdminAuditRead], granted);
        Assert.Empty(administrator.FindConfigurationErrors(SettingPath));
    }

    /// <summary>A name nothing publishes is a grant nobody enforces, so it keeps the refusal it always had, now named under the administrator.</summary>
    [Fact]
    public void FindConfigurationErrors_AGrantNamingAnUnpublishedPermission_IsRefusedUnderTheAdministrator()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add("mailfathom.admin.write");

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Equal(
            $"{SettingPath}:Permissions — 'mailfathom.admin.write' is not a permission MailFathom publishes; write one of {PublishedAdministrativeNames()}, or a pattern over them writing '*' in place of one or more whole segments.",
            reported);
    }

    /// <summary>A mail permission would sit in the file granting nothing while an operator believed they had granted something.</summary>
    [Fact]
    public void FindConfigurationErrors_AGrantNamingAMailPermission_IsRefusedAsTheOtherSurfaces()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add(MailFathomPermission.MailRead.Name);

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Equal(
            $"{SettingPath}:Permissions — '{MailFathomPermission.MailRead.Name}' belongs to the other protected surface and grants nothing here; write one of {PublishedAdministrativeNames()}, or move the entry to the endpoint that serves it.",
            reported);
    }

    [Fact]
    public void FindConfigurationErrors_ASubtreeOfTheMailSurface_IsRefused()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add("mailfathom.mail.*");

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains("matches only permissions of the other protected surface", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ASubtreeNothingIsPublishedBeneath_IsRefused()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add("mailfathom.post.*");

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("'mailfathom.post.*' matches no permission MailFathom publishes", reported, StringComparison.Ordinal);
    }

    /// <summary>Both spellings say what leaving the key out already says, and a second spelling would leave two arrangements meaning one thing.</summary>
    [Theory]
    [InlineData("*")]
    [InlineData("mailfathom.*")]
    public void FindConfigurationErrors_AGrantReachingBothSurfaces_IsRefused(string written)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add(written);

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"'{written}' reaches both protected surfaces entirely", reported, StringComparison.Ordinal);
        Assert.Contains("Remove the key", reported, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mailfathom.admin.read", "mailfathom.admin.read")]
    [InlineData("mailfathom.admin.audit.read", "mailfathom.admin.audit.*")]
    [InlineData("mailfathom.admin.*", "mailfathom.admin.audit.*")]
    public void FindConfigurationErrors_AValueRepeatingWhatTheGrantAlreadyCarries_IsRefused(string first, string second)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add(first);
        administrator.Permissions.Add(second);

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"'{second}'", reported, StringComparison.Ordinal);
        Assert.Contains("the grant already carries", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AWildcardInsideASegment_IsRefusedAsAnUnpublishedName()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.Permissions.Add("mailfathom.admin.a*");

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("is not a permission MailFathom publishes", reported, StringComparison.Ordinal);
    }

    /// <summary>A key, a public key, and a password carry no scope, so asking a token to narrow the grant beside one is a question nothing could answer.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FindConfigurationErrors_TokenScopeNarrowingBesideAConfiguredCredential_IsRefused(bool statesAnApiKey)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            ConfiguredAuthentication.OAuthFor("https://mail.example.test/api/admin"),
            statesAnApiKey ? ConfiguredAuthentication.ApiKey("alice") : ConfiguredAuthentication.PublicKey("alice"));
        administrator.PermissionsFromTokenScopes = true;

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith($"{SettingPath}:PermissionsFromTokenScopes — a token's scopes can narrow a grant", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_TokenScopeNarrowingOnAnAdministratorSigningInByTokenAlone_ReportsNothing()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            ConfiguredAuthentication.OAuthFor("https://mail.example.test/api/admin"));
        administrator.PermissionsFromTokenScopes = true;

        // Act, Assert
        Assert.Empty(administrator.FindConfigurationErrors(SettingPath));
    }

    /// <summary>The Basic refusal keeps its wording and is named under the credential that carried the block.</summary>
    [Fact]
    public void FindConfigurationErrors_ACredentialCarryingABasicBlock_IsRefusedUnderTheCredential()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            new AdministratorCredentialOptions { Basic = new BasicAuthenticationOptions() });

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith(
            $"{SettingPath}:Credentials:0:Basic — the administrative endpoint does not accept a user's username and password.",
            reported,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("192.0.2.10")]
    [InlineData("2001:db8::/32")]
    public void FindConfigurationErrors_AnAllowedSourceNetworkNamingAnAddressOrANetwork_ReportsNothing(string written)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.AllowedSourceNetworks.Add(written);

        // Act, Assert
        Assert.Empty(administrator.FindConfigurationErrors(SettingPath));
        Assert.True(administrator.RestrictsSourceNetworks);
    }

    /// <summary>The same rules the trusted proxies are read by, including the host-bits check, so one list is never read two ways.</summary>
    [Theory]
    [InlineData("vpn.example.test", "is neither an IP address nor a CIDR network")]
    [InlineData("10.0.0.0/33", "is not a CIDR network")]
    [InlineData("10.1.2.3/8", "names an address inside '10.0.0.0/8'")]
    [InlineData(" ", "an empty entry names no network")]
    public void FindConfigurationErrors_AnUnusableAllowedSourceNetwork_IsRefusedAtTheList(string written, string expected)
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.AllowedSourceNetworks.Add(written);

        // Act
        var errors = administrator.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.StartsWith($"{SettingPath}:AllowedSourceNetworks", reported, StringComparison.Ordinal);
        Assert.Contains(expected, reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AnAddressInsideANetworkWithHostBitsSet_SuggestsBothRemedies()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.AllowedSourceNetworks.Add("10.1.2.3/8");

        // Act
        var reported = Assert.Single(administrator.FindConfigurationErrors(SettingPath));

        // Assert
        Assert.Contains("Write '10.0.0.0/8' to admit that whole range", reported, StringComparison.Ordinal);
        Assert.Contains("drop the prefix to admit the one address", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ADnsName_SaysWhyItCannotStandInForAnAddress()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.AllowedSourceNetworks.Add("vpn.example.test");

        // Act
        var reported = Assert.Single(administrator.FindConfigurationErrors(SettingPath));

        // Assert
        Assert.Contains("a DNS name cannot stand in for one", reported, StringComparison.Ordinal);
    }

    /// <summary>A single address is a network of one, and an IPv4 address written in its mapped form is compared as the IPv4 address it is.</summary>
    [Fact]
    public void SourceNetworks_AnAddressAndAMappedAddress_AreEachANetworkOfOneInTheirComparableForm()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");
        administrator.AllowedSourceNetworks.Add("192.0.2.10");
        administrator.AllowedSourceNetworks.Add("::ffff:198.51.100.7");
        administrator.AllowedSourceNetworks.Add("2001:db8::1");

        // Act
        var networks = administrator.SourceNetworks();

        // Assert
        Assert.Equal(
            [
                new IPNetwork(IPAddress.Parse("192.0.2.10"), 32),
                new IPNetwork(IPAddress.Parse("198.51.100.7"), 32),
                new IPNetwork(IPAddress.Parse("2001:db8::1"), 128),
            ],
            networks);
    }

    [Fact]
    public void RestrictsSourceNetworks_AnEmptyList_LeavesTheAdministratorUnrestricted()
    {
        // Arrange
        var administrator = ConfiguredAuthentication.AdministratorWithApiKey("alice");

        // Act, Assert
        Assert.False(administrator.RestrictsSourceNetworks);
        Assert.Empty(administrator.SourceNetworks());
    }

    private static string PublishedAdministrativeNames() =>
        string.Join(
            ", ",
            MailFathomPermission.PublishedFor(ProtectedSurface.Administration).Select(permission => $"'{permission.Name}'"));
}
