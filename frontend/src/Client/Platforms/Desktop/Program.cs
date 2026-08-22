// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Uno.UI.Hosting;

namespace MailFathom.Client;

/// <summary>The desktop head's entry point. It hosts the same <see cref="App"/> every other head does.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
