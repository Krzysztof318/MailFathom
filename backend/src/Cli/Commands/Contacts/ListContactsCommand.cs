// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Reads one bounded page of the deployment's contact book.</summary>
/// <remarks>
/// <para>
/// One page per invocation, and the operator asks for the next. There is deliberately no command that walks the whole
/// book: a contact book printed in one call is every correspondent of a person's mailbox on one screen and in one shell
/// history, and paging is what makes reading it an act rather than a side effect of asking who is in it.
/// </para>
/// <para>
/// The order is the deployment's own — the name's comparison form, then the identity — which is total, so a walk serves
/// every contact exactly once. The cursor a page prints is what continues it.
/// </para>
/// <para>
/// A page is read over every book that user reads at once: their own, and the collected book of each mail account they
/// are assigned. Where two of those hold one address the page serves it once, from the user's own book first and then
/// from the accounts in the order of their identifiers. The hiding is applied as the page is read rather than to a page
/// already served, so the cursor rather than the count is what says whether more is waiting.
/// </para>
/// </remarks>
internal static class ListContactsCommand
{
    /// <summary>Builds the <c>contact list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<string?> originOption = new("--origin")
        {
            Description =
                "Narrow to contacts of one origin: Asserted for the people written down, Collected for the addresses the mailboxes picked up. Defaults to every book the user reads.",
        };

        var pageSizeOption = CliOptions.PageSize("contacts");
        var cursorOption = CliOptions.Cursor();

        Command command = new("list", "Read one page of the books one user reads.")
        {
            userOption,
            originOption,
            pageSizeOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(originOption),
            result.GetValue(pageSizeOption),
            result.GetValue(cursorOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        string? origin,
        int? pageSize,
        string? cursor,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(deployment, profile.Token, requestedUser, cancellationToken);
        var page = await deployment.ReadContactPageAsync(
            profile.Token,
            user,
            origin,
            pageSize,
            cursor,
            cancellationToken);

        if (page.Contacts is not { Count: > 0 } contacts)
        {
            context.Console.WriteLine(DescribeEmptyPage(origin, cursor));

            return CliExitCode.Success;
        }

        ContactOutput.WriteListing(context.Console, contacts);

        if (page.NextCursor is { Length: > 0 } continuation)
        {
            context.Console.WriteLine(string.Empty);
            context.Console.WriteLine($"More contacts follow. Continue with --cursor {continuation}");
        }

        return CliExitCode.Success;
    }

    /// <summary>States that the page held nobody, and what that usually means for the way it was asked.</summary>
    /// <remarks>
    /// A continued walk reaching an empty page is the end of the book rather than an empty book, and telling the two
    /// apart is what stops an operator from reading a completed walk as a deployment that lost its contacts.
    /// </remarks>
    private static string DescribeEmptyPage(string? origin, string? cursor) => (origin, cursor) switch
    {
        (_, { Length: > 0 }) => "That cursor reached the end of the books, so there was nothing further to read.",
        ({ Length: > 0 } narrowed, _) =>
            $"The books that user reads hold no {narrowed} contacts. Nothing is written down unless somebody writes it, and nothing is collected unless an account they are assigned has collection switched on.",
        _ =>
            "The books that user reads are empty. They hold nobody until somebody is recorded in the user's own book, or until an account they are assigned collects an address out of mail.",
    };
}
