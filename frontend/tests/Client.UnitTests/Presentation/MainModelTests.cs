// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;

namespace MailFathom.Client.UnitTests.Presentation;

public class MainModelTests
{
    [Fact]
    public async Task Build_TheScaffoldModel_YieldsTheRunningBuild()
    {
        await using var model = new MainModel();

        var build = await model.Build;

        Assert.Equal(ClientBuild.Current, build);
    }
}
