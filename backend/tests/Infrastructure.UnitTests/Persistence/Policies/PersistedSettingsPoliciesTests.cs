// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Policies;

/// <summary>
/// Covers what the store refuses before it issues a statement. What the statements themselves do is PostgreSQL's to
/// prove, and belongs to the integration suite.
/// </summary>
public sealed class PersistedSettingsPoliciesTests
{
    private static readonly Guid Organization = new("22222222-2222-4222-8222-222222222222");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"Users\"")]
    public async Task CommitAsync_ACandidateThatIsNotAJsonObject_IsRefusedBeforeAnyStatement(string json)
    {
        // Arrange
        await using var context = CreateContext();
        var store = new PersistedSettingsPolicies(context, new FakeTimeProvider());

        // Act
        var refusal = await Record.ExceptionAsync(() => store.CommitAsync(
            Organization,
            json,
            expectedVersion: 0,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentException>(refusal, exactMatch: false);
    }

    /// <summary>A policy past what a read hands back would be a row nobody could open again to correct.</summary>
    [Fact]
    public async Task CommitAsync_ACandidatePastTheCeiling_IsRefusedNamingBothFigures()
    {
        // Arrange
        await using var context = CreateContext();
        var store = new PersistedSettingsPolicies(context, new FakeTimeProvider());
        var oversized = $$$$"""{"Users":{"Defaults":{"Language":"{{{{new string('a', SettingsPolicyDocument.MaximumOctets)}}}}"}}}""";

        // Act
        var refusal = await Record.ExceptionAsync(() => store.CommitAsync(
            organizationId: null,
            oversized,
            expectedVersion: 3,
            TestContext.Current.CancellationToken));

        // Assert
        var argument = Assert.IsType<ArgumentException>(refusal);
        Assert.Contains($"occupies {RootSettingsCommitRules.PersistedOctetsOf(oversized)} octets", argument.Message, StringComparison.Ordinal);
        Assert.Contains($"past the {SettingsPolicyDocument.MaximumOctets}", argument.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_ANegativeVersion_IsRefusedBeforeAnyStatement()
    {
        // Arrange
        await using var context = CreateContext();
        var store = new PersistedSettingsPolicies(context, new FakeTimeProvider());

        // Act
        var refusal = await Record.ExceptionAsync(() => store.CommitAsync(
            organizationId: null,
            "{}",
            expectedVersion: -1,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }

    [Fact]
    public void Unwritten_AScopeStoringNoPolicy_StatesNothingAtTheVersionItsFirstWriteIsComposedOver()
    {
        // Arrange
        var expected = new SettingsPolicyDocument(Organization, "{}", Version: 0);

        // Act
        var policy = SettingsPolicyDocument.Unwritten(Organization);

        // Assert
        Assert.Equal(expected, policy);
    }

    private static MailFathomDbContext CreateContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
