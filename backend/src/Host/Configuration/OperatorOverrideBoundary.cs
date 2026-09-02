// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Provisioning;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration;

/// <summary>Finds where the deployment's own sources stop and the operator's overrides begin.</summary>
/// <remarks>
/// <para>
/// Two layers MailFathom adds — the files a deployment provisioned and the settings it persisted — belong below every
/// source an operator reaches for when a deployment is wrong: .NET User Secrets, unprefixed environment variables, and
/// command-line arguments. That direction is the one an operator can act on. Injecting one variable then changes one
/// setting for one process without editing a shared object or reaching the database, while the reverse would let a
/// stale mount or a bad persisted value silently beat a value injected beside it, with nothing about the running
/// process showing which of the two won.
/// </para>
/// <para>
/// Only the unprefixed environment provider is part of that boundary. The prefixed ones carry <c>DOTNET_</c> and
/// <c>ASPNETCORE_</c> host settings and are composed before the application's own files, so inserting ahead of those
/// would place both layers below <c>appsettings.json</c> and invert the whole point of adding them.
/// </para>
/// <para>
/// User Secrets is recognized by the file name the framework adds it under, which is the only thing that distinguishes
/// it: it is an ordinary JSON source, and the deployment's own JSON sources are named for the file each one reads. A
/// file name is something a deployment chooses, so the name alone would not settle it — a provisioned file called
/// <c>secrets.json</c> resolves to that same bare name and would be read as an override, placing both MailFathom
/// layers below it. What settles it is the type: the provisioned layer constructs
/// <see cref="ProvisionedJsonConfigurationSource" />, which this recognizes as the deployment's however it is named.
/// </para>
/// </remarks>
internal static class OperatorOverrideBoundary
{
    /// <summary>The file name .NET User Secrets is layered in under, whichever store directory holds it.</summary>
    private const string UserSecretsFileName = "secrets.json";

    /// <summary>Finds the index a layer is inserted at to sit directly below the operator's overrides.</summary>
    /// <param name="sources">The configuration sources composed so far.</param>
    /// <returns>The index to insert at, which is past the end when nothing composed an override source.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A host that composed no override source has nothing for the layer to sit below, so the layer takes the highest
    /// precedence rather than being dropped in at a position that would mean something else.
    /// </remarks>
    public static int FindIn(IReadOnlyList<IConfigurationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .Index()
            .Where(source => IsAnOperatorOverride(source.Item))
            .Select(source => source.Index)
            .DefaultIfEmpty(sources.Count)
            .First();
    }

    private static bool IsAnOperatorOverride(IConfigurationSource source) => source switch
    {
        EnvironmentVariablesConfigurationSource { Prefix: null or "" } => true,

        // Ahead of the JSON arm below, which it would otherwise match: a provisioned file is the deployment's
        // whatever the deployment named it.
        ProvisionedJsonConfigurationSource => false,
        JsonConfigurationSource json => string.Equals(json.Path, UserSecretsFileName, StringComparison.Ordinal),

        // Command-line arguments are an operator's override and are still not the boundary, because the boundary is
        // the *lowest* override rather than any of them: the layer goes in below the first one and thereby below all
        // of them. Recognizing the command line would change nothing where a host builder composes it last, and would
        // do harm where one composes it early — the layer would land below the deployment's own files, which is the
        // inversion this class exists to prevent. Everything else is the deployment's, and stays beneath the layer.
        _ => false,
    };
}
