// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

public sealed class PersistedDefaultAdministratorTests
{
    /// <summary>
    /// The administrator's row is written with both switches off, and a row is rewritten from what its record
    /// resolves to, so a record silent about them would hand the administrator both mail endpoints on the next
    /// write of a policy governing it.
    /// </summary>
    [Fact]
    public void ProvisionedRecord_TheRecordTheAdministratorStartsWith_StatesBothMailEndpointSwitchesOff()
    {
        // Arrange
        using var record = JsonDocument.Parse(PersistedDefaultAdministrator.ProvisionedRecord);

        // Act
        var switches = record.RootElement.GetProperty("EndpointAccess");

        // Assert
        Assert.Equal("false", switches.GetProperty("McpEndpoint").GetString());
        Assert.Equal("false", switches.GetProperty("ClientEndpoint").GetString());
    }
}
