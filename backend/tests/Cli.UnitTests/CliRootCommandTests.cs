// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Reflection;
using MailFathom.Cli.Commands;
using MailFathom.Cli.Credentials;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the tree the published binary answers to, against the commands this assembly actually declares.</summary>
/// <remarks>
/// <para>
/// The command has no container, so a dependency it forgot to register is not a shape this boundary can fail in. What it
/// fails in instead is a command that was written and never attached: the class compiles, its tests pass because they
/// call its factory directly, and <c>mfctl</c> answers the verb with an unrecognized-command error. Nothing else here
/// would notice, because the only reference a command needs in order to exist is the one <see cref="CliRootCommand" />
/// deliberately did not make.
/// </para>
/// <para>
/// That is why these two assertions enumerate types rather than naming them. A test listing the commands it expects is
/// a second copy of the tree, and the defect it has to catch is precisely a command absent from a list somebody was
/// maintaining by hand; the assembly's own type set is the one description of the commands that cannot be forgotten to
/// update. Reflection is the measured requirement the C# conventions ask for before reaching for it, and it stays
/// inside this file rather than reaching any production type.
/// </para>
/// </remarks>
public sealed class CliRootCommandTests
{
    /// <summary>Every command class this assembly publishes, paired with the command it builds.</summary>
    /// <remarks>
    /// Discovered by shape rather than by name: a static class under the commands namespace declaring
    /// <c>Create(CliContext)</c> is what a command is here, and building each one also proves its factory runs.
    /// <see cref="CliRootCommand" /> is excluded because it is the tree rather than a command in it.
    /// </remarks>
    private static IReadOnlyList<Command> DeclaredCommands()
    {
        var context = InertContext();

        return
        [
            .. typeof(CliRootCommand).Assembly
                .GetTypes()
                .Where(static type => type.Namespace?.StartsWith(typeof(CliRootCommand).Namespace!, StringComparison.Ordinal) is true)
                .Where(static type => type != typeof(CliRootCommand))
                .Select(static type => type.GetMethod(
                    nameof(CliRootCommand.Create),
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    [typeof(CliContext)]))
                .OfType<MethodInfo>()
                .Where(static factory => typeof(Command).IsAssignableFrom(factory.ReturnType))
                .OrderBy(static factory => factory.DeclaringType!.FullName, StringComparer.Ordinal)
                .Select(factory => (Command)factory.Invoke(obj: null, [context])!),
        ];
    }

    /// <summary>The commands the tree ends in, which are the ones a factory produced.</summary>
    /// <remarks>A group is built inline by <see cref="CliRootCommand" /> and has children; a command a factory built has none, so an empty group would fail this comparison as the defect it is rather than passing as a group.</remarks>
    private static IEnumerable<Command> LeafCommands(Command command) =>
        command.Subcommands.Count == 0
            ? [command]
            : command.Subcommands.SelectMany(LeafCommands);

    /// <summary>A context no command reaches through, because nothing here runs an action.</summary>
    /// <remarks>
    /// The store is pointed at a path it never reads and the transport throws, so building the tree touches neither the
    /// file system nor a socket. Both hold only while these tests build commands rather than invoke them, which is the
    /// line this class stays on.
    /// </remarks>
    private static CliContext InertContext() => new(
        new RecordingCliConsole(),
        new CredentialStore("credentials.json", new TokenProtector("credentials.key")),
        static (_, _) => throw new InvalidOperationException("A command tree was built, so no transport is opened."),
        FakeMailboxRedirect.Silent(),
        static _ => false,
        TimeProvider.System);

    /// <summary>
    /// The assertion this class exists for. A command class that nothing attached is unreachable from the binary, and
    /// the comparison is over a multiset rather than a set because two commands legitimately share a name under
    /// different groups — <c>status</c> and <c>run-status</c> both do — so a set would let an unattached one hide
    /// behind its twin.
    /// </summary>
    [Fact]
    public void Create_PublishesEveryCommandTheAssemblyDeclares()
    {
        // Arrange
        var declared = DeclaredCommands()
            .Select(static command => command.Name)
            .OrderBy(static name => name, StringComparer.Ordinal);

        // Act
        var published = LeafCommands(CliRootCommand.Create(InertContext()))
            .Select(static command => command.Name)
            .OrderBy(static name => name, StringComparer.Ordinal);

        // Assert
        Assert.Equal(declared, published);
    }

    /// <summary>
    /// Two siblings sharing a name is the other way a command becomes unreachable, and the parser reports nothing: it
    /// resolves the first match, so the second one's options are parsed against the first one's action.
    /// </summary>
    [Fact]
    public void Create_GivesEverySiblingCommandItsOwnName()
    {
        // Arrange
        var root = CliRootCommand.Create(InertContext());

        // Act
        var duplicated = Groups(root)
            .SelectMany(static group => group.Subcommands
                .GroupBy(static subcommand => subcommand.Name, StringComparer.Ordinal)
                .Where(static named => named.Count() > 1)
                .Select(named => $"{group.Name} {named.Key}"));

        // Assert
        Assert.Empty(duplicated);
    }

    /// <summary>The root and every command beneath it that carries children.</summary>
    private static IEnumerable<Command> Groups(Command command) =>
        command.Subcommands.Count == 0
            ? []
            : [command, .. command.Subcommands.SelectMany(Groups)];
}
