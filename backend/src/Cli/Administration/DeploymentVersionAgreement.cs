// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Commands;

namespace MailFathom.Cli.Administration;

/// <summary>Settles what the version a deployment reports means for the command about to act on it.</summary>
/// <remarks>
/// <para>
/// The version pair already answers whether the two builds agree, so nothing further has to be published for this:
/// within <c>0.x</c> a minor release may break any public surface and a patch may break none, which makes the
/// <c>major.minor</c> line the whole of the compatibility statement. A command from another line is therefore refused
/// rather than sent, and one from the same line carrying a different patch or prerelease identifier is a build
/// difference worth naming and nothing more. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md">ADR 0004</see>.
/// </para>
/// <para>
/// A version that cannot be read warns instead of refusing. The refusal reports an incompatibility that was observed,
/// and an unstamped or locally built binary is an absence of evidence rather than evidence of a break — refusing on it
/// would make a build nobody can identify a build nobody can administer.
/// </para>
/// </remarks>
internal sealed record DeploymentVersionAgreement
{
    private DeploymentVersionAgreement(bool permitsCommands, string? concern)
    {
        this.PermitsCommands = permitsCommands;
        this.Concern = concern;
    }

    /// <summary>Gets a value indicating whether the command may act on the deployment at all.</summary>
    internal bool PermitsCommands { get; }

    /// <summary>Gets the sentence the operator is told, which is <see langword="null" /> only where the two report the same version.</summary>
    /// <remarks>It is the refusal's own message where <see cref="PermitsCommands" /> is <see langword="false" />, and a warning the command carries on past where it is <see langword="true" />.</remarks>
    internal string? Concern { get; }

    /// <summary>Settles a command's own version against the one a deployment reports.</summary>
    /// <param name="commandVersion">The version this command was stamped with.</param>
    /// <param name="deploymentVersion">The version the deployment reports, which is <see langword="null" /> where it reported none.</param>
    /// <returns>What that pair permits, and what the operator is told about it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="commandVersion" /> is <see langword="null" />.</exception>
    internal static DeploymentVersionAgreement Settle(string commandVersion, string? deploymentVersion)
    {
        ArgumentNullException.ThrowIfNull(commandVersion);

        var command = commandVersion.Trim();
        var deployment = deploymentVersion?.Trim() ?? string.Empty;

        var commandLine = LineOf(command);
        var deploymentLine = LineOf(deployment);

        if (commandLine is null || deploymentLine is null)
        {
            return Permitting(Unreadable(command, deployment, commandLine is null, deploymentLine is null));
        }

        if (commandLine != deploymentLine)
        {
            return Refusing(command, deployment);
        }

        return string.Equals(command, deployment, StringComparison.Ordinal)
            ? new DeploymentVersionAgreement(permitsCommands: true, concern: null)
            : Permitting(DifferingBuilds(command, deployment));
    }

    private static DeploymentVersionAgreement Permitting(string concern) => new(permitsCommands: true, concern);

    private static DeploymentVersionAgreement Refusing(string command, string deployment) =>
        new(
            permitsCommands: false,
            $"{CliRootCommand.CommandName} is {Reported(command)} and the deployment is {Reported(deployment)}. A minor release is permitted to change the administrative contract, so a command is refused rather than sent to a deployment from another release line. Run the {CliRootCommand.CommandName} published with that deployment's release, or upgrade the deployment to this one.");

    private static string DifferingBuilds(string command, string deployment) =>
        $"{CliRootCommand.CommandName} is {Reported(command)} and the deployment is {Reported(deployment)}. They are the same release line and so agree on the administrative contract, but they are not the same build and problems may occur.";

    /// <summary>Says which of the two could not be read, because that is what decides where the operator looks.</summary>
    private static string Unreadable(
        string command,
        string deployment,
        bool commandUnreadable,
        bool deploymentUnreadable)
    {
        var subject = (commandUnreadable, deploymentUnreadable) switch
        {
            (true, true) => "Neither version is one",
            (true, false) => $"{CliRootCommand.CommandName}'s own version is not one",
            _ => "The deployment's version is not one",
        };

        return $"{subject} this command can compare: {CliRootCommand.CommandName} is {Reported(command)} and the deployment is {Reported(deployment)}. Whether the two agree on the administrative contract is unchecked, so problems may occur.";
    }

    /// <summary>Reads the release line a version belongs to, which is the whole of what the comparison acts on.</summary>
    /// <remarks>
    /// A stamped version carries a prerelease identifier, and — before
    /// <see cref="Versioning.StampedAssemblyVersion" /> has split one off — build metadata as well:
    /// <c>0.5.0-nightly.41</c> is the same line as <c>0.5.0</c>, because a nightly is a preview of the release it will
    /// become rather than a version of its own. Both are cut away before the numeric part is read, and anything left
    /// that is not a version — <c>unknown</c> above all — reads as no line at all rather than as a line that differs
    /// from every other. What remains needs the two components that name the line and no more, so a value stating
    /// exactly that is read rather than refused on a formality.
    /// </remarks>
    private static (int Major, int Minor)? LineOf(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return null;
        }

        var core = version.AsSpan();
        var identifierStart = core.IndexOfAny('-', '+');

        if (identifierStart >= 0)
        {
            core = core[..identifierStart];
        }

        return Version.TryParse(core, out var parsed) ? (parsed.Major, parsed.Minor) : null;
    }

    private static string Reported(string version) => version.Length == 0 ? "reporting none" : version;
}
