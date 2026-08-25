// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
/// it: it is an ordinary JSON source, and the deployment's own JSON sources are named for the file each one reads.
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
        JsonConfigurationSource json => string.Equals(json.Path, UserSecretsFileName, StringComparison.Ordinal),
        _ => false,
    };
}
