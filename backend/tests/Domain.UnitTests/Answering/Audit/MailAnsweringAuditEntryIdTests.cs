// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Answering.Audit;
using Xunit;

namespace MailFathom.Domain.UnitTests.Answering.Audit;

/// <summary>Covers what addresses one entry of an answering record.</summary>
public sealed class MailAnsweringAuditEntryIdTests
{
    [Fact]
    public void Create_ANonEmptyValue_KeepsIt()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var entryId = MailAnsweringAuditEntryId.Create(value);

        // Assert
        Assert.Equal(value, entryId.Value);
        Assert.Equal(value.ToString(), entryId.ToString());
    }

    /// <summary>An empty value names no entry, and a cursor built from one would name no boundary.</summary>
    [Fact]
    public void Create_AnEmptyValue_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailAnsweringAuditEntryId.Create(Guid.Empty));
    }
}
