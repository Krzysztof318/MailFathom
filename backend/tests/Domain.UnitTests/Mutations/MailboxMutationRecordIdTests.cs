// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class MailboxMutationRecordIdTests
{
    [Fact]
    public void Create_EmptyUuid_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRecordId.Create(Guid.Empty));

        // Assert
        Assert.Equal("value", refusal.ParamName);
    }

    [Fact]
    public void Create_TheSameUuidTwice_ProducesEqualIdentifiers()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var identifier = MailboxMutationRecordId.Create(value);

        // Assert
        Assert.Equal(MailboxMutationRecordId.Create(value), identifier);
        Assert.Equal(value.ToString(), identifier.ToString());
    }
}
