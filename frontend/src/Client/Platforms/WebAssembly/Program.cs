// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Platforms.WebAssembly;
using Uno.UI.Hosting;

namespace MailFathom.Client;

/// <summary>The browser head's entry point. It hosts the same <see cref="App"/> every other head does.</summary>
internal static class Program
{
    private static async Task Main()
    {
        var host = UnoPlatformHostBuilder.Create()
            // The two things this head composes differently. It was served by its deployment, so it reaches whatever
            // served it rather than an address somebody had to state; and it keeps no credential at all, because every
            // store a browser offers is scoped to the page's origin rather than to a person, so anything running on
            // that origin would read an owner's password. Everything else about the application is the same code the
            // desktop head runs.
            .App(() => new App(new PageOriginDeploymentAddress(), UnkeptOwnerCredentialStore.Instance))
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
