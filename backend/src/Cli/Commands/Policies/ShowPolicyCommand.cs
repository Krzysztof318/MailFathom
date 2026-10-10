// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Policies;

/// <summary>Reads one scope's settings policy as the deployment holds it.</summary>
/// <remarks>
/// What an administrator answers "what does this deployment, or this organization, state for the users and the mail
/// accounts beneath it" from. Nothing in the document is redacted, because a policy holds no secret, so what is printed
/// is what is stored. A scope that stores no policy answers with an empty document at version zero, and that is printed
/// as it arrived rather than reported as an absence: it is what the first edit is composed over.
/// </remarks>
internal static class ShowPolicyCommand
{
    /// <summary>Builds the <c>policy show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var formatOption = CliOptions.DocumentFormat();
        var organizationOption = PolicyOptions.Organization();

        Command command = new("show", "Read the settings policy this deployment holds, or the one an organization holds.")
        {
            organizationOption,
            formatOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(formatOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? organizationId,
        DocumentView view,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var policy = await deployment.ReadSettingsPolicyAsync(profile.Token, organizationId, cancellationToken);

        // The scope is named from the answer rather than from the invocation, so what the heading says is whose policy
        // the deployment handed over.
        context.Console.WriteLine(policy.OrganizationId is { } organization
            ? $"Settings policy of organization {organization:D}"
            : "Settings policy of the deployment");
        context.Console.WriteLine($"  version: {policy.Version.ToString(CultureInfo.InvariantCulture)}");
        context.Console.WriteLine(EditorDrivenDocument.Show(policy.Document ?? "{}", view));

        return CliExitCode.Success;
    }
}
