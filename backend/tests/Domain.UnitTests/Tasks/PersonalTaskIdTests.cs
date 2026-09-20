// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>Covers the one value a task identifier cannot hold, and the one it can reach without the factory.</summary>
public sealed class PersonalTaskIdTests
{
    /// <summary>An empty UUID addresses nothing, so it is refused where every other value is wrapped.</summary>
    [Fact]
    public void Create_AnEmptyUuid_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => PersonalTaskId.Create(Guid.Empty));
    }

    /// <summary>Being a struct, the default is reachable without the factory, and this is what reports it.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_NamesNoTask()
    {
        // Act
        var identifier = default(PersonalTaskId);

        // Assert
        Assert.False(identifier.IsSpecified);
    }

    /// <summary>A wrapped UUID names a task and reads back as the value it was created from.</summary>
    [Fact]
    public void Create_ANonEmptyUuid_NamesATaskAndKeepsItsValue()
    {
        // Arrange
        var value = Guid.NewGuid();

        // Act
        var identifier = PersonalTaskId.Create(value);

        // Assert
        Assert.True(identifier.IsSpecified);
        Assert.Equal(value, identifier.Value);
        Assert.Equal(value.ToString(), identifier.ToString());
    }
}
