// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Connections;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Connections;

public sealed class DatabaseHostSetTests
{
    [Theory]
    [InlineData("Host=one.example.test;Database=mailfathom", false)]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes=primary", true)]
    public void NamesSeveralInstances_HostKeyword_ReportsWhetherTheDeploymentNamedMoreThanOne(
        string connectionString,
        bool expected)
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);

        // Act
        var namesSeveral = DatabaseHostSet.NamesSeveralInstances(connectionSettings);

        // Assert
        Assert.Equal(expected, namesSeveral);
    }

    [Fact]
    public void BuildDataSource_OneInstance_BuildsTheSingleHostPool()
    {
        // Arrange
        var dataSourceBuilder = new NpgsqlDataSourceBuilder("Host=one.example.test;Database=mailfathom");

        // Act
        using var dataSource = DatabaseHostSet.BuildDataSource(dataSourceBuilder);

        // Assert
        Assert.IsNotType<NpgsqlMultiHostDataSource>(dataSource);
    }

    [Fact]
    public void BuildDataSource_SeveralInstances_BuildsThePoolThatFindsThePrimaryAmongThem()
    {
        // Arrange
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(
            "Host=one.example.test,two.example.test,three.example.test;Database=mailfathom;Target Session Attributes=primary");

        // Act
        using var dataSource = DatabaseHostSet.BuildDataSource(dataSourceBuilder);

        // Assert
        Assert.IsType<NpgsqlMultiHostDataSource>(dataSource);
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("Primary")]
    [InlineData("read-write")]
    public void RefuseAHostSetThatCouldSettleOnAStandby_SeveralInstancesTargetingAWritableSession_Accepts(
        string targetSessionAttributes)
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder(
            $"Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes={targetSessionAttributes}");

        // Act
        var refusal = Record.Exception(() => DatabaseHostSet.RefuseAHostSetThatCouldSettleOnAStandby(connectionSettings));

        // Assert
        Assert.Null(refusal);
    }

    [Theory]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom")]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes=any")]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes=prefer-primary")]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes=prefer-standby")]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes=standby")]
    [InlineData("Host=one.example.test,two.example.test;Database=mailfathom;Target Session Attributes=read-only")]
    public void RefuseAHostSetThatCouldSettleOnAStandby_SeveralInstancesPermittingAStandby_Refuses(string connectionString)
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => DatabaseHostSet.RefuseAHostSetThatCouldSettleOnAStandby(connectionSettings));

        // Assert
        Assert.Contains("Target Session Attributes", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("primary", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("read-write", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefuseAHostSetThatCouldSettleOnAStandby_SeveralInstancesPermittingAStandby_NamesNoPartOfTheConnectionString()
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder(
            "Host=one.example.test,two.example.test;Database=mailfathom;Username=mailfathom;Password=not-a-real-password");

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => DatabaseHostSet.RefuseAHostSetThatCouldSettleOnAStandby(connectionSettings));

        // Assert
        Assert.DoesNotContain("not-a-real-password", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("one.example.test", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefuseAHostSetThatCouldSettleOnAStandby_OneInstance_AcceptsWhateverTheTargetSessionAttributesSay()
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder(
            "Host=one.example.test;Database=mailfathom;Target Session Attributes=any");

        // Act
        var refusal = Record.Exception(() => DatabaseHostSet.RefuseAHostSetThatCouldSettleOnAStandby(connectionSettings));

        // Assert
        Assert.Null(refusal);
    }
}
