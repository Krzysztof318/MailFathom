// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves a settings policy is read through a bound the statement applies, rather than one applied to what arrived.</summary>
/// <remarks>
/// Only a real server settles this: the bound is <c>octet_length</c> over PostgreSQL's own rendering of the column,
/// and the row is found by a query composed over that statement. The policy is written for an organization the test
/// created and removes, which takes the policy with it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSettingsPolicyReadTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ForcingPolling = """{"MailAccounts":{"Forced":{"Mode":"Polling"}}}""";

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A policy within the bound is handed on as it was stored, and the same row once it holds a document past the
    /// bound is refused naming whose it is — which no write through the store can arrange, so the row is overfilled
    /// beside it, as a hand or a restored backup would.
    /// </summary>
    [Fact]
    public async Task ReadAsync_APolicyPastWhatThisBuildReads_IsRefusedRatherThanRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();

        try
        {
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                        organization,
                        $"Organization {organization:N}",
                        OrganizationShortName.Create($"{organization:N}"),
                        RecordedAt,
                        token),
                    cancellationToken)).Outcome);

            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>()
                    .CommitAsync(organization, ForcingPolling, expectedVersion: 0, token),
                cancellationToken);

            var withinTheBound = await ReadAsync(services, organization, cancellationToken);

            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .SettingsPolicies
                    .Where(policy => policy.OrganizationId == organization)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(policy => policy.Document, DocumentPastTheCeiling()), token),
                cancellationToken);

            // Act
            var refused = await Record.ExceptionAsync(() => ReadAsync(services, organization, cancellationToken));

            // Assert
            Assert.Equal(1, withinTheBound?.Version);
            Assert.Contains("\"Polling\"", withinTheBound?.Json, StringComparison.Ordinal);

            var unreadable = Assert.IsType<SettingsPolicyUnreadableException>(refused);
            Assert.Equal(MailFathomErrorCode.SettingsPolicyUnreadable, unreadable.ErrorCode);
            Assert.Contains($"organization {organization:D}", unreadable.Message, StringComparison.Ordinal);
        }
        finally
        {
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    private static Task<SettingsPolicyDocument?> ReadAsync(
        OrchestratedMailFathomServices services,
        Guid organizationId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ISettingsPolicyStore>().ReadAsync(organizationId, token),
            cancellationToken);

    /// <summary>Composes one policy larger than the ceiling, as one value rather than as many.</summary>
    /// <remarks>The margin keeps it past the bound whichever of the compact form and the stored rendering is measured.</remarks>
    private static string DocumentPastTheCeiling() =>
        $$"""{"Filler":"{{new string('f', SettingsPolicyDocument.MaximumOctets + 1024)}}"}""";
}
