// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;

namespace MailFathom.Client.UnitTests.Presentation;

public sealed class MainModelTests
{
    /// <summary>Awaiting a feed is how a model's state is asserted in this stack, and this one proves the MVUX path behind the only screen reaches the running build.</summary>
    [Fact]
    public async Task Build_TheScaffoldModel_YieldsTheRunningBuild()
    {
        // Arrange
        await using var model = new MainModel();

        // Act
        var build = await model.Build;

        // Assert
        Assert.Equal(ClientBuild.Current, build);
    }
}
