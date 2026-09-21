// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Records;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers what an operator is told about the records this deployment will not read. Each of them costs only itself, so
/// nothing else would report that it happened — which is the whole reason this route exists.
/// </summary>
public sealed class HeldBackRecordEndpointsTests
{
    private static readonly Guid OrganizationId = new("44444444-4444-4444-8444-444444444444");
    private static readonly Guid MailAccountId = new("0197a3c0-0000-7000-8000-000000000001");

    /// <summary>A deployment reading every record it holds answers three empty lists rather than nothing at all.</summary>
    [Fact]
    public async Task ReadAsync_NothingHeldBack_AnswersThreeEmptyLists()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var answer = await HeldBackRecordEndpoints.ReadAsync(
            harness.HeldBack,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(answer.Value!.Users);
        Assert.Empty(answer.Value.MailAccounts);
        Assert.Empty(answer.Value.Organizations);
    }

    /// <summary>Each kind is reported apart, because the remedy differs: a record, an account, and a short name.</summary>
    [Fact]
    public async Task ReadAsync_OneOfEachKindHeldBack_ReportsEachUnderItsOwnKind()
    {
        // Arrange
        var harness = new EndpointHarness();

        harness.HeldBack.Replace(
            SyntheticUser.Deployment,
            [
                new HeldBackRecord(
                    HeldBackRecordKind.User,
                    SyntheticUser.Deployment.Value,
                    "alex",
                    RejectedVersion: 4,
                    ["Correct the record."]),
                new HeldBackRecord(
                    HeldBackRecordKind.MailAccount,
                    MailAccountId,
                    "work",
                    RejectedVersion: 2,
                    ["Correct the account."]),
            ]);

        harness.Organizations.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new OrganizationListing([], [new UnreadableOrganization(OrganizationId, "Acme", "Correct it.")]));

        // Act
        var answer = await HeldBackRecordEndpoints.ReadAsync(
            harness.HeldBack,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticUser.Deployment.Value, Assert.Single(answer.Value!.Users).Id);
        Assert.Equal(4, Assert.Single(answer.Value.Users).RejectedVersion);
        Assert.Equal(MailAccountId, Assert.Single(answer.Value.MailAccounts).Id);
        Assert.Equal(OrganizationId, Assert.Single(answer.Value.Organizations).Id);
    }

    /// <summary>An organization row carries no version of its own, so the answer says so rather than inventing one.</summary>
    [Fact]
    public async Task ReadAsync_AnUnreadableOrganization_ReportsNoRejectedVersion()
    {
        // Arrange
        var harness = new EndpointHarness();

        harness.Organizations.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new OrganizationListing([], [new UnreadableOrganization(OrganizationId, "Acme", "Correct it.")]));

        // Act
        var answer = await HeldBackRecordEndpoints.ReadAsync(
            harness.HeldBack,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var organization = Assert.Single(answer.Value!.Organizations);
        Assert.Null(organization.RejectedVersion);
        Assert.Equal(["Correct it."], organization.Corrections);
    }

    private sealed class EndpointHarness
    {
        internal EndpointHarness()
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(AuthorizedPrincipal.Caller("operations", [MailFathomPermission.AdminRead]));

            this.Organizations = Substitute.For<IOrganizationStore>();
            this.Organizations.ReadAsync(Arg.Any<CancellationToken>())
                .Returns(new OrganizationListing([], []));

            this.Administration = new OrganizationAdministration(
                new AccessAuthorization(principals),
                this.Organizations,
                new FakeTimeProvider(new DateTimeOffset(2026, 9, 14, 7, 0, 0, TimeSpan.Zero)));
        }

        internal HeldBackRecords HeldBack { get; } = new();

        internal OrganizationAdministration Administration { get; }

        internal IOrganizationStore Organizations { get; }
    }
}
