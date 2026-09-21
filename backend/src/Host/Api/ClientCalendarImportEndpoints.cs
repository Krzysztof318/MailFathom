// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Import;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Takes an iCalendar file the signed-in person chose, reports what it holds, and writes it once they confirm.</summary>
/// <remarks>
/// <para>
/// <b>Two routes over one act, and the first of them writes nothing.</b> A file somebody else prepared is exactly the
/// case where having chosen it says little about what it would create, so the summary is what a person accepts rather
/// than a courtesy laid over an import that was going to happen anyway. Confirming is the second route, and it reads
/// the same file the same way.
/// </para>
/// <para>
/// <b>The confirmation carries the file again rather than a token.</b> Nothing is staged between the two calls, so
/// there is no half-import to expire or to clean up and no entry of somebody's day held here while they decide. The
/// cost is one more upload of a file already bounded, which is the cheaper half of that trade.
/// </para>
/// <para>
/// <b>Reading a file is not subscribing to a calendar.</b> Nothing here reaches a calendar server, and nothing an
/// entry names causes an outbound request of any kind: the reader takes six properties of an entry and never its
/// attachments, its URLs, or the addresses it names.
/// </para>
/// <para>
/// <b>Both routes are bounded twice.</b> The octets are refused by the routing pipeline above
/// <see cref="CalendarFileImport.MaximumFileBytes" />, and a file naming more than
/// <see cref="CalendarFileImport.MaximumEntryCount" /> entries is refused whole rather than truncated — a person
/// confirming a summary must be deciding about everything the file holds, and a truncated import would be one they
/// were never shown.
/// </para>
/// <para>
/// <b>Nothing the file carries reaches a refusal.</b> Every message stated here comes from this system, so a title, a
/// date, or an identifier out of somebody else's schedule is never echoed into a problem document a proxy log keeps.
/// </para>
/// </remarks>
internal static class ClientCalendarImportEndpoints
{
    /// <summary>The route a chosen file is imported at, relative to the client prefix.</summary>
    internal const string CalendarImportRoute = $"{ClientCalendarEndpoints.CalendarRoute}/import";

    /// <summary>The route a chosen file is read and reported on without being written, relative to the client prefix.</summary>
    /// <remarks>
    /// A segment under the import rather than a flag on it, because the two are different acts to a reader of an
    /// access log and to whoever is reasoning about which requests can change a calendar: one of these never does.
    /// </remarks>
    internal const string CalendarImportSummaryRoute = $"{CalendarImportRoute}/summary";

    /// <summary>The media type RFC 5545 registers for an iCalendar file.</summary>
    private const string CalendarMediaType = "text/calendar";

    /// <summary>The media type a browser sends for a file whose kind the operating system does not name.</summary>
    /// <remarks>
    /// Accepted beside the registered one because a browser picking a file off disk reports whatever is associated
    /// with the extension, which is frequently nothing at all. Neither is trusted: what the file is gets decided by
    /// parsing it, exactly as a portrait's kind is decided by its signature rather than by what the request declared.
    /// </remarks>
    private const string UnnamedMediaType = "application/octet-stream";

    /// <summary>Maps the import routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientCalendarImport(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as every other upload on this
        // surface reaches it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the
        // request body feature, so a file over the bound is refused before its octets are buffered.
        api.MapPost(CalendarImportSummaryRoute, SummariseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(CalendarFileImport.MaximumFileBytes))
            .Accepts<Stream>(CalendarMediaType, UnnamedMediaType)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPost(CalendarImportRoute, ImportAsync)
            .WithMetadata(new RequestSizeLimitAttribute(CalendarFileImport.MaximumFileBytes))
            .Accepts<Stream>(CalendarMediaType, UnnamedMediaType)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Reports what the offered file would put on the acting person's calendar, writing nothing.</summary>
    /// <param name="import">Reads the file against the calendar of the person the credential names.</param>
    /// <param name="context">The request being answered, whose body carries the file.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with what the file would create and skip, <c>413</c> for a file over the bound, or <c>400</c> naming why the file was refused.</returns>
    internal static async Task<Results<Ok<CalendarImportResponse>, ProblemHttpResult>> SummariseAsync(
        [FromServices] CalendarFileImport import,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);

        return await AnswerAsync(
            context,
            (file, token) => import.SummariseAsync(file, token),
            cancellationToken);
    }

    /// <summary>Puts the events of the offered file onto the acting person's calendar.</summary>
    /// <param name="import">Performs the write against the calendar of the person the credential names.</param>
    /// <param name="context">The request being answered, whose body carries the file.</param>
    /// <param name="cancellationToken">Cancels the read and the commit when the client disconnects.</param>
    /// <returns><c>200</c> with what the file created and skipped, <c>413</c> for a file over the bound, or <c>400</c> naming why the file was refused.</returns>
    /// <remarks>
    /// What is written is asserted, because choosing a file and confirming what it holds is a person putting those
    /// events on their calendar. Either every event the answer names is on it or none is: the write is one
    /// transaction, so a file that fails part way through leaves nothing to find and undo by hand.
    /// </remarks>
    internal static async Task<Results<Ok<CalendarImportResponse>, ProblemHttpResult>> ImportAsync(
        [FromServices] CalendarFileImport import,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);

        return await AnswerAsync(
            context,
            (file, token) => import.ImportAsync(file, token),
            cancellationToken);
    }

    /// <summary>Buffers the offered file, hands it to the act asked for, and states any refusal in this system's own words.</summary>
    /// <remarks>
    /// Buffered rather than streamed because the reader takes the whole file, which the bound above is what makes
    /// safe. The pipeline's own refusal of an oversized body carries no text at all, and the one thing a person needs
    /// from it is the bound they went over, so it is restated here as a problem rather than left as a bare status.
    /// </remarks>
    private static async Task<Results<Ok<CalendarImportResponse>, ProblemHttpResult>> AnswerAsync(
        HttpContext context,
        Func<Stream, CancellationToken, Task<CalendarImportSummary>> act,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var file = new MemoryStream();

        try
        {
            await context.Request.Body.CopyToAsync(file, cancellationToken);
        }
        catch (BadHttpRequestException refusal)
            when (refusal.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Refuse(
                $"An imported calendar file is at most {CalendarFileImport.MaximumFileBytes / 1024 / 1024} MB.",
                StatusCodes.Status413PayloadTooLarge);
        }

        if (file.Length == 0)
        {
            return Refuse("The request carries no file, so there is nothing to import.");
        }

        file.Position = 0;

        var summary = await act(file, cancellationToken);

        return summary.Outcome is CalendarImportOutcome.Read
            ? TypedResults.Ok(CalendarImportResponse.For(summary))
            : Refuse(Stated(summary.Outcome));
    }

    /// <summary>States what a caller has to change, without echoing anything the file carries.</summary>
    private static string Stated(CalendarImportOutcome outcome) => outcome switch
    {
        CalendarImportOutcome.NotCalendarData =>
            "The file is not iCalendar, or is iCalendar this deployment could not finish reading. An import takes a "
            + "single .ics file as a calendar application exports one.",
        CalendarImportOutcome.TooManyEntries =>
            $"An import reads at most {CalendarFileImport.MaximumEntryCount} entries, and this file names more. It is "
            + "refused whole rather than partly written, so nothing was put on the calendar.",
        _ => "The file cannot be imported as offered.",
    };

    /// <summary>States what a caller has to change, without echoing anything about the file it offered.</summary>
    private static ProblemHttpResult Refuse(string stated, int statusCode = StatusCodes.Status400BadRequest) =>
        TypedResults.Problem(stated, statusCode: statusCode);
}
