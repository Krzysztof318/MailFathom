// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Reads the organizations this deployment holds.</summary>
/// <remarks>
/// The listing every other organization command takes its identifiers out of, and the one place an operator reads the
/// short name a member's login begins with and whether an organization can be removed yet.
/// </remarks>
internal static class ListOrganizationsCommand
{
    /// <summary>Builds the <c>organization list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the organizations this deployment holds.")
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
        var deployment = new AdminApiClient(transport, context.Console);

        var listing = await deployment.ReadOrganizationsAsync(profile.Token, cancellationToken);

        if (listing.Organizations is { Count: > 0 } organizations)
        {
            context.Console.Write(Draw(organizations));
        }
        else if (listing.Unreadable is not { Count: > 0 })
        {
            context.Console.WriteLine(
                "This deployment holds no organizations. Record one with 'organization add', then move a user into it "
                + "with 'user set-organization'.");

            return CliExitCode.Success;
        }

        ReportUnreadable(context, listing.Unreadable);

        return CliExitCode.Success;
    }

    /// <summary>Says which rows the deployment will not read as an organization, and what each must become.</summary>
    /// <remarks>
    /// <para>
    /// Printed after the listing rather than mixed into it, because these rows are not organizations the deployment
    /// serves: their members cannot type the prefix their login begins with, and every other organization is unaffected.
    /// Nothing here is the stored short name — it is the value that failed every rule, so the identifier names the row
    /// and the sentence says what to write with 'organization set-short-name'.
    /// </para>
    /// <para>
    /// Both halves are reduced before they are written, as every other name and sentence a listing prints is: the
    /// display name is whatever an operator recorded and nothing rejects a control character in one, and the sentence
    /// arrived over the wire. A terminal acts on an escape sequence rather than printing it, so a name could otherwise
    /// clear the screen or rewrite the line of the operator reading it.
    /// </para>
    /// </remarks>
    private static void ReportUnreadable(CliContext context, IReadOnlyList<UnreadableOrganizationEntry>? unreadable)
    {
        if (unreadable is not { Count: > 0 })
        {
            return;
        }

        context.Console.WriteLine(string.Empty);
        context.Console.WriteLine(
            $"{unreadable.Count} organization rows are held back because their stored short name is not one this "
            + "deployment reads. Their members cannot sign in with the prefix their login begins with; every other "
            + "organization is unaffected. Repair each with 'organization set-short-name'.");

        foreach (var organization in unreadable)
        {
            context.Console.WriteLine(
                $"  {organization.Id:D} ({ConsoleSafeText.Sanitize(organization.DisplayName) ?? "unreported"}): "
                + (ConsoleSafeText.Sanitize(organization.Correction) ?? "unreported"));
        }
    }

    private static CliTable Draw(IReadOnlyList<OrganizationEntry> organizations)
    {
        CliTable listing = new("Organization", "Short name", "Display name", "Members", "Recorded");

        foreach (var organization in organizations)
        {
            listing.AddRow(
                $"{organization.Id:D}",
                ConsoleSafeText.Sanitize(organization.ShortName) ?? "unreported",
                ConsoleSafeText.Sanitize(organization.DisplayName) ?? "unreported",
                organization.Members.ToString(CultureInfo.InvariantCulture),
                $"{organization.CreatedAt:u}");
        }

        return listing;
    }
}
