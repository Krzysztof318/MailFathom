// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;

namespace MailFathom.Client;

/// <summary>
/// What a build of the client reports about itself: the product it belongs to and the version it was stamped with.
/// </summary>
/// <remarks>
/// The version is read from the assembly rather than written anywhere in this stack, because <c>Version.props</c> at
/// the repository root is the one place a version number is stated and both stacks stamp their assemblies from it. A
/// client and a service that reported two numbers would be describing one product twice.
/// </remarks>
/// <param name="Product">The product name the assembly was built under.</param>
/// <param name="Version">The version the assembly was stamped with, without the build metadata a pipeline appends.</param>
public sealed record ClientBuild(string Product, string Version)
{
    /// <summary>The build the running client was produced by.</summary>
    public static ClientBuild Current { get; } = FromAssembly(typeof(ClientBuild).Assembly);

    /// <summary>Reads the product and version an assembly was stamped with.</summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <returns>What that assembly reports about itself.</returns>
    public static ClientBuild FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? string.Empty;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? string.Empty;

        // The SDK composes the informational version as `<version>+<commit>` wherever a source revision is known, so
        // the metadata is cut off here rather than shown: it names the commit rather than the release.
        var buildMetadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        var version = buildMetadata < 0 ? informationalVersion : informationalVersion[..buildMetadata];

        return new ClientBuild(product, version);
    }
}
