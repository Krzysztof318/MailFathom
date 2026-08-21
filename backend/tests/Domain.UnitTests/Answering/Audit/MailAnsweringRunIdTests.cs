// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Answering.Audit;
using Xunit;

namespace MailFathom.Domain.UnitTests.Answering.Audit;

/// <summary>Covers the identity every entry of one answering run shares.</summary>
public sealed class MailAnsweringRunIdTests
{
    [Fact]
    public void Create_ANonEmptyValue_KeepsIt()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var runId = MailAnsweringRunId.Create(value);

        // Assert
        Assert.Equal(value, runId.Value);
        Assert.Equal(value.ToString(), runId.ToString());
    }

    /// <summary>An empty value names no run, so it is refused where it is created rather than stored and read back.</summary>
    [Fact]
    public void Create_AnEmptyValue_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailAnsweringRunId.Create(Guid.Empty));
    }
}
