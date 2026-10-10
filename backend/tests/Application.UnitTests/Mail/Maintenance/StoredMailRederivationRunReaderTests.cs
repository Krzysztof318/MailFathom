// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Maintenance;

/// <summary>Covers whose re-derivation runs an administrator holding the read over one organization is shown.</summary>
public sealed class StoredMailRederivationRunReaderTests
{
    private static readonly StoredMailScope WholeAccount = new(MailAccountId.Create("0198f0aa-0000-7000-8000-0000000000e1"), null);

    private readonly InMemoryStoredMailRederivationRunStore runs = new();

    public StoredMailRederivationRunReaderTests() => this.runs.Arrange(new StoredMailRederivationRun
    {
        RunId = StoredMailRederivationRunId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000e2")),
        Scope = WholeAccount,
        RequestedAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
        SegmentCount = 1,
    });

    /// <summary>A run of a mailbox in another organization is refused naming the permission, and nothing about the run is read.</summary>
    [Fact]
    public async Task FindAsync_AnAccountOutsideTheCallersOrganization_IsRefusedNamingThePermission()
    {
        // Arrange
        var reader = new StoredMailRederivationRunReader(
            this.runs,
            OrganizationAdministrators.Holding(
                MailFathomPermission.AdminRead,
                WholeAccount.Account,
                OrganizationAdministrators.OtherOrganization));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.FindAsync(WholeAccount, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    [Fact]
    public async Task FindAsync_AnAccountInTheCallersOrganization_AnswersWithItsRun()
    {
        // Arrange
        var reader = new StoredMailRederivationRunReader(
            this.runs,
            OrganizationAdministrators.Holding(
                MailFathomPermission.AdminRead,
                WholeAccount.Account,
                OrganizationAdministrators.AdministeredOrganization));

        // Act
        var run = await reader.FindAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(WholeAccount, run?.Scope);
    }
}
