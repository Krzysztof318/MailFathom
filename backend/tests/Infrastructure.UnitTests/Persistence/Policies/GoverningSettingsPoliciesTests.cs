// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Policies;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Policies;

public sealed class GoverningSettingsPoliciesTests
{
    private static readonly Guid Organization = Guid.Parse("0197c0de-0000-7000-8000-0000000000a1");
    private static readonly Guid AnotherOrganization = Guid.Parse("0197c0de-0000-7000-8000-0000000000a2");

    private static readonly SettingsPolicyDocument[] Stored =
    [
        new(OrganizationId: null, """{"MailAccounts":{"Defaults":{"Port":1993}}}""", Version: 4),
        new(Organization, """{"MailAccounts":{"Forced":{"Mode":"Push"}}}""", Version: 2),
        new(AnotherOrganization, """{"MailAccounts":{"Forced":{"Mode":"Polling"}}}""", Version: 7),
    ];

    /// <summary>
    /// One read answers the policies of several organizations, so a record takes its own organization's out of them
    /// and never the one stored beside it.
    /// </summary>
    [Fact]
    public void Among_ARecordOfOneOrganization_TakesTheDeploymentsPolicyAndThatOrganizationsAlone()
    {
        // Act
        var governing = GoverningSettingsPolicies.Among(Stored, Organization);

        // Assert
        Assert.Equal(4, governing.Deployment.Version);
        Assert.Equal(Organization, governing.Organization?.OrganizationId);
        Assert.Equal(2, governing.Organization?.Version);
    }

    /// <summary>A record in no organization is governed by the deployment's policy alone, whatever else the read found stored.</summary>
    [Fact]
    public void Among_ARecordOfNoOrganization_TakesTheDeploymentsPolicyAndNoOrganizations()
    {
        // Act
        var governing = GoverningSettingsPolicies.Among(Stored, organizationId: null);

        // Assert
        Assert.Equal(4, governing.Deployment.Version);
        Assert.Null(governing.Organization);
    }

    /// <summary>A scope that stores no policy is answered as one stating nothing, at the version that says it was never written.</summary>
    [Fact]
    public void Among_ScopesThatStoreNoPolicy_AreAnsweredAsStatingNothing()
    {
        // Act
        var governing = GoverningSettingsPolicies.Among([], Organization);

        // Assert
        Assert.Equal(SettingsPolicyDocument.Unwritten(organizationId: null), governing.Deployment);
        Assert.Equal(SettingsPolicyDocument.Unwritten(Organization), governing.Organization);
    }
}
