// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;

namespace MailFathom.Cli.Authorization;

/// <summary>Opens an address in whatever browser this machine considers the default.</summary>
/// <remarks>
/// A convenience and never a requirement. The address is printed either way, so a machine with no browser, no desktop
/// session, or no <c>xdg-open</c> loses nothing but a click — which is what makes the interactive flow usable over an
/// SSH session where the port is forwarded and the browser is somewhere else entirely.
/// </remarks>
internal static class WebBrowserLauncher
{
    /// <summary>Tries to open an address, and reports whether the attempt was made.</summary>
    /// <param name="address">The address to open.</param>
    /// <returns><see langword="true" /> when a browser was started.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <c>UseShellExecute</c> is what hands the address to the platform's own handler rather than trying to execute it,
    /// and it is the only shape that works on both the platforms the command ships for. Every failure is swallowed:
    /// there is no outcome here worth failing an authorization over.
    /// </remarks>
    internal static bool TryOpen(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });

            return browser is not null;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
