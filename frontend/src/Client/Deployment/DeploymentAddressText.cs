// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Client.Deployment;

/// <summary>How an address somebody wrote becomes one this client can be pointed at.</summary>
/// <remarks>
/// <para>
/// Written once because it is read in two places that must agree: a value in an installation's own configuration file
/// and a value somebody typed into the screen that asks for one. Two readers with two ideas of what
/// <c>mail.example.test</c> means would be a client that reached a different deployment depending on where the same
/// text was written.
/// </para>
/// <para>
/// HTTPS is the default rather than a requirement stated twice. A value written without a scheme is read as HTTPS,
/// because that is what a deployment reached across a network is served over and because the alternative — reading it
/// as clear text — would turn an omission into an exposure. Whether the result may be used at all is
/// <c>DeploymentAddressRule</c>'s question and is not answered here, so one rule judges every address whoever wrote it.
/// </para>
/// </remarks>
internal static class DeploymentAddressText
{
    /// <summary>What a value with no scheme is read as.</summary>
    private const string DefaultScheme = "https";

    /// <summary>Reads written text as the address it names.</summary>
    /// <param name="written">What somebody wrote, which may be blank or nonsense.</param>
    /// <param name="address">The address it names, where it names one.</param>
    /// <returns><see langword="true" /> when the text names an absolute address; <see langword="false" /> when it is blank or unreadable.</returns>
    internal static bool TryRead(string? written, [NotNullWhen(true)] out Uri? address)
    {
        var stated = written?.Trim() ?? string.Empty;

        if (stated.Length == 0)
        {
            address = null;

            return false;
        }

        // Scheme-relative and schemeless are the same case here: the deployment is named by an origin, so anything
        // before the host that is not a scheme is not something this can repair.
        var addressed = stated.Contains("://", StringComparison.Ordinal)
            ? stated
            : $"{DefaultScheme}://{stated.TrimStart('/')}";

        return Uri.TryCreate(addressed, UriKind.Absolute, out address);
    }
}
