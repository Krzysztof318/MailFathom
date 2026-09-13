// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Organizations;

/// <summary>Covers which grant admits each act on organizations, and what recording one mints.</summary>
public sealed class OrganizationAdministrationTests
{
    private static readonly MailUserId User = MailUserId.Create(new Guid("0197c0de-0000-4000-8000-000000000001"));

    private static readonly Guid OrganizationId = new("0197c0de-0000-4000-8000-000000000002");

    [Fact]
    public async Task CreateAsync_ACallerGrantedTheConfigurationWrite_RecordsTheTrimmedNameUnderAMintedVersion4Identifier()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Organizations.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<OrganizationShortName>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(OrganizationWriteResult.Of(OrganizationWriteOutcome.Written));

        // Act
        var result = await harness.Administration.CreateAsync(
            "  Test Firma  ",
            OrganizationShortName.Create("testfirma"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.Written, result.Outcome);
        Assert.Equal(4, result.OrganizationId.Version);

        await harness.Organizations.Received(1).CreateAsync(
            result.OrganizationId,
            "Test Firma",
            OrganizationShortName.Create("TESTFIRMA"),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Moving a user changes the login every password of theirs is typed as, so the configuration grant alone does not reach it.</summary>
    [Fact]
    public async Task SetUserOrganizationAsync_ACallerGrantedOnlyTheConfigurationWrite_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.SetUserOrganizationAsync(User, OrganizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Organizations.ReceivedCalls());
    }

    [Fact]
    public async Task DeleteAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.DeleteAsync(OrganizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateAsync_ADisplayNameTheBoundaryShouldHaveRefused_Raises(string? displayName)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var act = () => harness.Administration.CreateAsync(
            displayName,
            OrganizationShortName.Create("ACME"),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    private sealed class AdministrationHarness
    {
        internal AdministrationHarness(MailFathomPermission granted)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(AuthorizedPrincipal.Caller("operations", [granted]));

            this.Organizations = Substitute.For<IOrganizationStore>();

            this.Administration = new OrganizationAdministration(
                new AccessAuthorization(principals),
                this.Organizations,
                new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)));
        }

        internal OrganizationAdministration Administration { get; }

        internal IOrganizationStore Organizations { get; }
    }
}
