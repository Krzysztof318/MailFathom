// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;
using MailFathom.Client.Platforms.Desktop.Credentials;
using Uno.UI.Hosting;

namespace MailFathom.Client;

/// <summary>The desktop head's entry point. It hosts the same <see cref="App"/> every other head does.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var host = UnoPlatformHostBuilder.Create()
            // The two things this head answers for itself: it is installed rather than served, so its deployment comes
            // from what somebody wrote beside it, and it has an operating system behind it, so the credential goes
            // where that operating system holds a secret for one user.
            .App(() => new App(
                new ConfiguredDeploymentAddress(),
                DesktopOwnerCredentialStore.ForThisOperatingSystem()))
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
