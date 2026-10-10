// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Common;

namespace MailFathom.Cli.Commands;

/// <summary>Reports what the deployment in use says about the stored credential.</summary>
/// <remarks>
/// <para>
/// It asks the deployment rather than reading the store, which is the point: the store says what was true at sign-in,
/// and this says whether the credential still works. It is therefore the command that tells an operator their key has
/// been revoked or has expired.
/// </para>
/// <para>
/// It is also where an operator learns where to read about what they are administering, and the version that decides
/// that is the deployment's rather than this command's: the two are separate builds, and a command from a nightly
/// pointed at a released deployment would otherwise name pages for something nobody is running. A deployment that
/// reports no version it can read is told nothing about documentation, which is the same absence of evidence
/// <see cref="DeploymentVersionAgreement" /> warns on rather than acts on.
/// </para>
/// </remarks>
internal static class StatusCommand
{
    /// <summary>Builds the <c>status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("status", "Check that the stored credential still works.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var session = await new AdminApiClient(transport, context.Console).ReadSessionAsync(profile.Token, cancellationToken);

        context.Console.WriteLine(
            $"'{profile.Name}' ({profile.Endpoint.GetLeftPart(UriPartial.Authority)}) accepts the stored credential as '{session.Credential}' (MailFathom {session.Version}).");

        var narrowerScopes = (session.Scopes ?? []).Where(scope => scope.Target is not null).ToArray();

        context.Console.WriteLine(DescribeGrant(session.Permissions, narrowerScopes.Length > 0));

        foreach (var scope in narrowerScopes)
        {
            context.Console.WriteLine(DescribeScope(scope));
        }

        if (DocumentationAddress.ForVersion(session.Version) is { } documentation)
        {
            context.Console.WriteLine($"Documentation for that version: {documentation}");
        }

        return CliExitCode.Success;
    }

    /// <summary>States what the credential may do, which is what decides whether any other command will work.</summary>
    /// <remarks>
    /// Reported here rather than left to be discovered one refusal at a time: an operator who has just signed in wants
    /// to know which commands are theirs before they run one. A credential granted nothing is the case worth stating
    /// plainly, because it is how one is retired without its entry being deleted and its sign-in still succeeds. A
    /// credential holding names only over an organization or a user is told what it lacks over the whole deployment,
    /// and the lines naming those scopes follow.
    /// </remarks>
    private static string DescribeGrant(IReadOnlyList<string>? permissions, bool holdsNarrowerScopes) => permissions switch
    {
        null => "The deployment did not state what the credential may do.",
        { Count: 0 } when holdsNarrowerScopes => "It holds no administrative permission over the whole deployment, so every operation but this one is refused.",
        { Count: 0 } => "It holds no administrative permission, so every operation but this one is refused.",
        _ => $"It holds {string.Join(", ", permissions)}.",
    };

    /// <summary>States what the credential is granted over one organization or one user, and that the endpoint admits none of it there.</summary>
    /// <remarks>
    /// The caveat sits on the scope's own line rather than on the first one, because the first line names what is held
    /// over the whole deployment and says nothing about a narrower grant once that list is not empty. A name only the
    /// deployment scope grants is named apart, so an operator granted a role carrying it over one organization is not
    /// left to believe it acts there.
    /// </remarks>
    private static string DescribeScope(AdminSessionScope scope)
    {
        var held = scope.Permissions is { Count: > 0 } permissions
            ? $"Over {scope.Scope} {scope.Target} it is granted {string.Join(", ", permissions)}, which this endpoint admits only at the deployment scope."
            : $"Over {scope.Scope} {scope.Target} it is granted only names the deployment scope alone grants.";

        return scope.ReachingNothing is { Count: > 0 } inert
            ? $"{held} {string.Join(", ", inert)} reaches nothing there, because only the deployment scope grants it."
            : held;
    }
}
