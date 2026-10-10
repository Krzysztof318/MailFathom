// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Policies;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

public sealed class GovernablePropertiesTests
{
    /// <summary>
    /// Describing a record refuses a setting no policy statement could govern, so this is the test that fails when a
    /// property added to a user's record or to a mail account's declaration is neither governable nor classed.
    /// </summary>
    [Fact]
    public void All_BothGovernedRecords_AreDescribedWithEveryPropertyGovernableOrClassed()
    {
        // Arrange
        var sections = SettingsPolicySection.All;

        // Act
        var described = sections.Select(section => section.Properties.All.Count);

        // Assert
        Assert.All(described, count => Assert.True(count > 0));
    }

    /// <summary>The classes are a decision per property, so the whole of it is pinned: a property joining or leaving one is a change somebody made on purpose.</summary>
    [Fact]
    public void All_AUsersRecord_ClassesItsIdentityAndAdministratorOnlyPropertiesAndLeavesTheRestOrdinary()
    {
        // Arrange
        var properties = SettingsPolicySection.Users.Properties;

        // Act
        var classed = ClassedIn(properties);

        // Assert
        Assert.Equal(
            [
                "ClientTelemetryLevel=AdministratorOnly",
                "DisplayName=Identity",
                "EndpointAccess=AdministratorOnly",
                "EndpointAccess:ClientEndpoint=AdministratorOnly",
                "EndpointAccess:McpEndpoint=AdministratorOnly",
                "Portrait=Identity",
            ],
            classed);
        Assert.Equal(SettingsPolicyPropertyClass.Ordinary, properties.Find("Language")!.PropertyClass);
        Assert.Equal(SettingsPolicyPropertyClass.Ordinary, properties.Find("TimeZone")!.PropertyClass);
    }

    [Fact]
    public void All_AMailAccount_ClassesItsIdentityPropertiesAndEverySecretBlock()
    {
        // Arrange
        var properties = SettingsPolicySection.MailAccounts.Properties;

        // Act
        var classed = ClassedIn(properties);
        var secrets = properties.All
            .Where(property => property.Shape == GovernablePropertyShape.Secret)
            .Select(property => property.Path)
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            [
                "Delivery:FromAddress=Identity",
                "Delivery:FromDisplayName=Identity",
                "Delivery:Secrets:Password=Identity",
                "Delivery:UserName=Identity",
                "DisplayName=Identity",
                "EmailAddress=Identity",
                "OAuth:ClientSecret=Identity",
                "OAuth:RefreshToken=Identity",
                "Secrets:Password=Identity",
                "TransportSecurity:TrustedCertificateAuthority=Identity",
                "UserName=Identity",
            ],
            classed);
        Assert.Equal(
            [
                "Delivery:Secrets:Password",
                "OAuth:ClientSecret",
                "OAuth:RefreshToken",
                "Secrets:Password",
                "TransportSecurity:TrustedCertificateAuthority",
            ],
            secrets);
    }

    [Theory]
    [InlineData("Host", nameof(GovernablePropertyShape.Value))]
    [InlineData("Folders", nameof(GovernablePropertyShape.List))]
    [InlineData("TransportSecurity:PermittedAuthenticationMechanisms", nameof(GovernablePropertyShape.List))]
    [InlineData("Delivery", nameof(GovernablePropertyShape.Block))]
    [InlineData("SensitiveContent", nameof(GovernablePropertyShape.Block))]
    public void Find_APropertyOfAMailAccount_IsShapedAsAPolicyResolvesIt(string path, string expected)
    {
        // Arrange
        var properties = SettingsPolicySection.MailAccounts.Properties;

        // Act
        var property = properties.Find(path);

        // Assert
        Assert.Equal(expected, property?.Shape.ToString());
    }

    /// <summary>A path is compared the way a path within a record is, and answered in the casing the type declares.</summary>
    [Fact]
    public void Find_APathInAnotherCasing_NamesThePropertyAsTheTypeDeclaresIt()
    {
        // Arrange
        var properties = SettingsPolicySection.Users.Properties;

        // Act
        var property = properties.Find("endpointaccess:MCPENDPOINT");

        // Assert
        Assert.Equal("EndpointAccess:McpEndpoint", property?.Path);
    }

    [Theory]
    [InlineData("MailAccounts")]
    [InlineData("AccountId")]
    public void Find_APropertyMarkedOutsideEveryPolicy_IsNotGovernableAndSaysWhy(string path)
    {
        // Arrange
        var properties = path == "AccountId"
            ? SettingsPolicySection.MailAccounts.Properties
            : SettingsPolicySection.Users.Properties;

        // Act
        var property = properties.Find(path);
        var reason = properties.FindReasonOutside(path);

        // Assert
        Assert.Null(property);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    /// <summary>A view computed from the settings is not one of them, so a policy naming it names nothing.</summary>
    [Fact]
    public void Find_AViewComputedFromTheSettings_IsNotASetting()
    {
        // Arrange
        var properties = SettingsPolicySection.MailAccounts.Properties;

        // Act
        var computed = properties.Find("EffectiveFolders");

        // Assert
        Assert.Null(computed);
    }

    [Fact]
    public void FindListReachedInto_APathBeneathAList_NamesTheList()
    {
        // Arrange
        var properties = SettingsPolicySection.MailAccounts.Properties;

        // Act
        var list = properties.FindListReachedInto("Folders:0:Alias");
        var none = properties.FindListReachedInto("Delivery:Nothing");

        // Assert
        Assert.Equal("Folders", list?.Path);
        Assert.Null(none);
    }

    [Theory]
    [InlineData("EndpointAccess", true)]
    [InlineData("EndpointAccess:McpEndpoint", true)]
    [InlineData("ClientTelemetryLevel", true)]
    [InlineData("Language", false)]
    public void HoldsAdministratorOnly_APropertyOrABlock_AnswersForEverythingBeneathIt(string path, bool expected)
    {
        // Arrange
        var properties = SettingsPolicySection.Users.Properties;

        // Act
        var holds = properties.HoldsAdministratorOnly(properties.Find(path)!);

        // Assert
        Assert.Equal(expected, holds);
    }

    [Fact]
    public void Of_ASettingOfAShapeNoStatementGoverns_RefusesToDescribeTheRecordNamingIt()
    {
        // Arrange
        var record = typeof(RecordWithAnUngovernableSetting);

        // Act
        var refusal = Record.Exception(() => GovernableProperties.Of(record));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(refusal);
        Assert.Contains(
            $"{nameof(RecordWithAnUngovernableSetting)}.{nameof(RecordWithAnUngovernableSetting.Position)}",
            invalid.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Of_ABlockThatContainsItself_RefusesToDescribeTheRecord()
    {
        // Arrange
        var record = typeof(BlockContainingItself);

        // Act
        var refusal = Record.Exception(() => GovernableProperties.Of(record));

        // Assert
        Assert.IsType<InvalidOperationException>(refusal);
    }

    [Fact]
    public void Of_ASampleRecord_GovernsWhatTheBinderWritesAndPassesOverWhatItCannot()
    {
        // Arrange
        var record = typeof(SampleRecord);

        // Act
        var properties = GovernableProperties.Of(record, "Label");
        var described = properties.All
            .OrderBy(property => property.Path, StringComparer.Ordinal)
            .Select(property => $"{property.Path}={property.Shape}/{property.PropertyClass}");

        // Assert
        Assert.Equal(
            [
                "Credential=Secret/Identity",
                "Entries=List/Ordinary",
                "Filled=List/Ordinary",
                "Held=Block/AdministratorOnly",
                "Held:Level=Value/AdministratorOnly",
                "Label=Value/Identity",
                "Limit=Value/Ordinary",
            ],
            described);
        Assert.Equal("somebody else's", properties.FindReasonOutside("Elsewhere"));
    }

    [Fact]
    public void ReadFrom_ARecordBoundSparsely_ReadsTheValueAndTheBlockThatDeclaresIt()
    {
        // Arrange
        var level = GovernableProperties.Of(typeof(SampleRecord)).Find("Held:Level")!;
        var record = new SampleRecord();
        record.Held.Level = 7;

        // Act
        var (owner, value) = level.ReadFrom(record);

        // Assert
        Assert.Same(record.Held, owner);
        Assert.Equal(7, value);
        Assert.Equal(typeof(int), level.ValueType);
    }

    private static IEnumerable<string> ClassedIn(GovernableProperties properties) =>
        properties.All
            .Where(property => property.PropertyClass != SettingsPolicyPropertyClass.Ordinary)
            .OrderBy(property => property.Path, StringComparer.Ordinal)
            .Select(property => $"{property.Path}={property.PropertyClass}");

    private sealed class SampleRecord
    {
        public int? Limit { get; set; }

        public List<string> Entries { get; set; } = [];

        public List<int> Filled { get; } = [];

        public IReadOnlyList<string> Computed => this.Entries;

        public int Count => this.Entries.Count;

        public ConfiguredSecret? Credential { get; set; }

        [SettingsPolicyClass(SettingsPolicyPropertyClass.AdministratorOnly)]
        public SampleBlock Held { get; } = new();

        [OutsideSettingsPolicy("somebody else's")]
        public string? Elsewhere { get; set; }
    }

    private sealed class SampleBlock
    {
        public int Level { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "A record is described from its type alone, so nothing constructs this one.")]
    private sealed class RecordWithAnUngovernableSetting
    {
        public Complex Position { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "A record is described from its type alone, so nothing constructs this one.")]
    private sealed class BlockContainingItself
    {
        public BlockContainingItself? Inner { get; set; }
    }
}
