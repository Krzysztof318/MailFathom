// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Configuration;

/// <summary>Names the store a writable configuration path is persisted in.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration rather than a C# <see langword="enum" /> because a route carries the name a
/// refusal, a projection, and a record are written against, and that name has to survive a rename of the member. What
/// it deliberately does not carry is a table: which relation a store lives in is the persistence adapter's decision
/// and is named where the adapter maps it, so no application-layer value has to be kept in step with a migration.
/// </para>
/// <para>
/// The set is closed because the deployment's settings must have exactly one home each. A path with no special route
/// is persisted in <see cref="RootDocument" />, a path a special route names is persisted in that store and excluded
/// from the root document, and there is no third answer for a reader to choose between.
/// </para>
/// <para>
/// Unlike the repository's other closed enumerations this one publishes no parser and no JSON converter, and the
/// absence is the point rather than an omission: a route named by a configuration value or by an API argument is
/// exactly what <see cref="ConfigurationStorageCatalog" /> exists to refuse, so the only way to reach a route is to
/// resolve a configuration path against the compiled catalog. Being a struct, <see langword="default" /> is reachable
/// and is not a route; <see cref="IsSpecified" /> reports it, and <see cref="ConfigurationWriteTarget" /> is where it
/// means a refused write rather than a store.
/// </para>
/// </remarks>
public readonly record struct ConfigurationStorageRoute
{
    private readonly string? name;

    private ConfigurationStorageRoute(string name) => this.name = name;

    /// <summary>Gets the store holding the deployment's single persisted configuration document.</summary>
    /// <remarks>This is where every path no special route names is persisted, so it is the answer for almost every setting.</remarks>
    public static ConfigurationStorageRoute RootDocument { get; } = new("root-document");

    /// <summary>Gets the store holding one configuration document per owner.</summary>
    /// <remarks>
    /// The top-level <c>Accounts</c> collection of owner accounts is routed here, one document per owner rather than
    /// one per mailbox, which is why those documents are not values inside the root document.
    /// </remarks>
    public static ConfigurationStorageRoute OwnerAccounts { get; } = new("owner-accounts");

    /// <summary>Gets every store this build persists configuration into.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ConfigurationStorageRoute> All { get; } = [RootDocument, OwnerAccounts];

    /// <summary>Gets whether this value names a store rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the route's name, which is stable across a rename of the member and reaches an operator's record.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a route.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a configuration store.");

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
