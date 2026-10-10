// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>Covers how many users an account may be assigned to, which is the bound every write of an assignment reads.</summary>
public sealed class MailAccountRecordTests
{
    /// <summary>Sharing a mailbox is something an organization does, so an account in none is one person's.</summary>
    [Fact]
    public void MaximumUsersAssignedIn_NoOrganization_IsOneUser()
    {
        // Act
        var mostUsers = MailAccountRecord.MaximumUsersAssignedIn(organizationId: null);

        // Assert
        Assert.Equal(1, mostUsers);
    }

    [Fact]
    public void MaximumUsersAssignedIn_AnOrganization_IsEverybodyAReadOfTheAccountReaches()
    {
        // Act
        var mostUsers = MailAccountRecord.MaximumUsersAssignedIn(new Guid("0197c0de-0000-4000-8000-0000000000c1"));

        // Assert
        Assert.Equal(MailAccountRecord.MaximumUsersAssigned, mostUsers);
    }
}
