// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Loader;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Every compiled boundary of the solution, read once and shared by every rule in this project.</summary>
/// <remarks>
/// <para>
/// The rules here read intermediate language rather than assembly metadata, which is the whole reason this project
/// exists: reflection reports what a member's signature names, so a capability obtained inside a method body and never
/// stored is invisible to it. Loading is deliberately paid for once, because it decompiles every type of every
/// assembly named below.
/// </para>
/// <para>
/// <c>ArchUnitNET.Domain</c> and <c>System.Reflection</c> both publish <c>Assembly</c>, so both are written out in
/// full here rather than one of them being imported and read as the other.
/// </para>
/// </remarks>
internal static class CompiledBoundaries
{
    /// <summary>
    /// The full name of the one type outside every adapter that still names the libraries they are built on, and the
    /// place where naming them is the point: composing an adapter means handing it a data source, a client factory, or
    /// a context configured with both. A rule confining a library to its adapter therefore admits this type and the
    /// compiler-generated classes the registration lambdas inside it become, whose full names begin with its own.
    /// </summary>
    internal const string RegistrationSurfacePattern = @"^MailFathom\.Infrastructure\.ServiceCollectionExtensions";

    internal static System.Reflection.Assembly AI { get; } = System.Reflection.Assembly.Load("MailFathom.AI");

    internal static System.Reflection.Assembly Application { get; } =
        System.Reflection.Assembly.Load("MailFathom.Application");

    /// <summary>The administrative command, whose assembly is named after the binary an operator invokes.</summary>
    internal static System.Reflection.Assembly Cli { get; } = System.Reflection.Assembly.Load("mfctl");

    internal static System.Reflection.Assembly Common { get; } =
        System.Reflection.Assembly.Load("MailFathom.Common");

    internal static System.Reflection.Assembly Domain { get; } =
        System.Reflection.Assembly.Load("MailFathom.Domain");

    internal static System.Reflection.Assembly Host { get; } = System.Reflection.Assembly.Load("MailFathom.Host");

    internal static System.Reflection.Assembly Infrastructure { get; } =
        System.Reflection.Assembly.Load("MailFathom.Infrastructure");

    internal static System.Reflection.Assembly Mcp { get; } = System.Reflection.Assembly.Load("MailFathom.Mcp");

    /// <summary>The eight assemblies a release ships, as one architecture every rule is checked against.</summary>
    internal static ArchUnitNET.Domain.Architecture Solution { get; } = new ArchLoader()
        .LoadAssemblies(AI, Application, Cli, Common, Domain, Host, Infrastructure, Mcp)
        .Build();
}
