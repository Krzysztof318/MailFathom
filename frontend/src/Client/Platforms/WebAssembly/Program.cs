// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Platforms.WebAssembly;
using Uno.UI.Hosting;

namespace MailFathom.Client;

/// <summary>The browser head's entry point. It hosts the same <see cref="App"/> every other head does.</summary>
internal static class Program
{
    private static async Task Main()
    {
        var host = UnoPlatformHostBuilder.Create()
            // The one thing this head composes differently: it was served by its deployment, so it reaches whatever
            // served it rather than an address somebody had to state. Everything else about the application is the
            // same code the desktop head runs.
            .App(() => new App(new PageOriginDeploymentAddress()))
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
