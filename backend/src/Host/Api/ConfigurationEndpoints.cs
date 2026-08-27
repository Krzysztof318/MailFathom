// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the deployment's own configuration: what it reads, where each value is decided, and how one changes.</summary>
/// <remarks>
/// <para>
/// Three paths, each read with <c>GET</c> and performed with <c>POST</c> where there is anything to perform. The first
/// reads the settings by path and takes keyed changes; the second hands over the persisted document itself and takes
/// it back edited; the third previews what the deployment's files decide beneath a path and takes that decision into
/// the database. Together they are the whole of what an operator does to configuration without opening the row.
/// </para>
/// <para>
/// <b>The readings are <c>mailfathom.admin.read</c> and every write is
/// <c>mailfathom.admin.configuration.write</c></b>, which is the one place this surface allocates a name for a single
/// group of routes. A persisted setting decides what the deployment is rather than what it does next: the same write
/// that corrects a search bound can widen a credential's grant or repoint a model provider, so a credential that may
/// operate this deployment must not thereby be able to redefine it.
/// </para>
/// <para>
/// Nothing here writes a setting itself. Every commit is one <see cref="IConfigurationWriter" /> call over one version,
/// so the deny-list, the route catalog, the secret rule, the candidate binding, the validators, and the version guard
/// judge a change reaching this surface exactly as they judge one reaching any other.
/// </para>
/// <para>
/// A deployment composing no persisted layer serves none of them. That is not a state a deployment reaches — the host
/// reads the layer before it composes anything else — but a host built from files alone, which is what a test composes,
/// would otherwise answer these routes with a resolution failure instead of the absence it actually has.
/// </para>
/// </remarks>
internal static class ConfigurationEndpoints
{
    /// <summary>The route the settings are read at and keyed changes are written to, relative to the administrative prefix.</summary>
    internal const string ConfigurationRoute = "/configuration";

    /// <summary>The route the persisted document is read at and saved back to.</summary>
    /// <remarks>
    /// A path of its own rather than a shape on the route above, because the two are different transactions: one names
    /// the settings it changes, and this one carries the whole document and is judged against the version it was
    /// opened over. A body carrying which of the two was meant would make a mistyped field the difference between
    /// changing one setting and replacing every one of them.
    /// </remarks>
    internal const string DocumentRoute = $"{ConfigurationRoute}/document";

    /// <summary>The route an adoption is previewed at and performed on.</summary>
    internal const string AdoptionRoute = $"{ConfigurationRoute}/adoption";

    /// <summary>The greatest request body the three write routes read before refusing it.</summary>
    /// <remarks>
    /// Twice what the persisted document may be, because a body larger than that can compose no document this
    /// deployment would accept: the layer refuses a candidate past
    /// <see cref="RootSettingsDocument.MaximumOctets" /> whatever it binds to, and the doubling is the room JSON string
    /// escaping and the request envelope take on the way. Stated because the server's own default is measured in tens
    /// of megabytes, which would let an authenticated client make the process buffer a body far larger than any
    /// configuration this deployment could hold.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 2 * RootSettingsDocument.MaximumOctets;

    /// <summary>Maps the configuration routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapConfiguration(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // Whether the service is registered, never the service itself. Mapping runs before the host has started, and
        // resolving this one would construct the persisted-settings reader and through it the connection provider,
        // which refuses to answer until a startup lifecycle event has composed the connection string — so asking for
        // the instance here would stop every deployment that composes a layer, which is all of them. The provider is
        // reached through the interface because that is where a group publishes it.
        if (((IEndpointRouteBuilder)api).ServiceProvider.GetService<IServiceProviderIsService>()
            is not { } registrations || !registrations.IsService(typeof(PersistedSettingsAdministration)))
        {
            return;
        }

        api.MapGet(ConfigurationRoute, Read)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(ConfigurationRoute, WriteAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapGet(DocumentRoute, ReadDocumentAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(DocumentRoute, SaveDocumentAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapGet(AdoptionRoute, ReadAdoptable)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(AdoptionRoute, AdoptAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);
    }

    /// <summary>Reports the deployment's settings at or beneath a path, with the layer each value came from.</summary>
    /// <param name="settings">The deployment's configuration administration.</param>
    /// <param name="prefix">The colon-delimited path to read beneath, or nothing for every setting the deployment composed.</param>
    /// <returns><c>200</c> with the settings, or <c>400</c> when the prefix matched more than one reading answers with.</returns>
    /// <remarks>
    /// A prefix matching nothing answers with an empty reading rather than <c>404</c>, because a setting nobody
    /// configured is a fact about the deployment rather than an address that does not exist — and it is the answer an
    /// operator checking whether a setting is set at all is asking for.
    /// </remarks>
    internal static Results<Ok<ConfigurationReadingResponse>, ProblemHttpResult> Read(
        [FromServices] PersistedSettingsAdministration settings,
        [FromQuery] string? prefix)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reading = settings.Read(prefix);

        return reading.IsTooBroad
            ? TooBroad(reading, prefix)
            : TypedResults.Ok(ConfigurationReadingResponse.For(settings.ComposedVersion, reading));
    }

    /// <summary>Applies keyed changes to the deployment's persisted configuration.</summary>
    /// <param name="settings">The deployment's configuration administration.</param>
    /// <param name="request">The changes and the version they were composed over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit, leaving the configuration unchanged unless the commit was already in flight.</param>
    /// <returns><c>200</c> with what the write did, or <c>400</c> when the request states no change this boundary accepts.</returns>
    /// <remarks>
    /// A change the boundary itself will not accept — an empty list, a path that names no setting, a value past the
    /// bound, either half carrying a NUL — is a caller's mistake and answers <c>400</c>. Every refusal about the
    /// configuration itself answers <c>200</c> with the outcome, because each is something the administrator acts on
    /// and continues from and each carries the version they compose the next attempt over.
    /// </remarks>
    internal static async Task<Results<Ok<ConfigurationWriteResponse>, ProblemHttpResult>> WriteAsync(
        [FromServices] PersistedSettingsAdministration settings,
        [FromBody] ConfigurationWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version < 0)
        {
            return Refusal("A configuration write states the version it was composed over, which is never negative.");
        }

        if (request.Changes is not { Count: > 0 } changes)
        {
            return Refusal("A configuration write states at least one change.");
        }

        if (changes.Count > IConfigurationWriter.MaximumEdits)
        {
            return Refusal(
                $"A configuration write carries at most {IConfigurationWriter.MaximumEdits} changes, and this one carries {changes.Count}.");
        }

        if (changes.Select(Unacceptable).FirstOrDefault(refusal => refusal is not null) is { } stated)
        {
            return Refusal(stated);
        }

        if (NamedTwice(changes) is { } repeated)
        {
            return Refusal(
                $"The change names '{repeated}' more than once, so what the write would leave at that path depends on which of them the deployment keeps. State one change per path.");
        }

        IReadOnlyList<ConfigurationEdit> edits;

        try
        {
            edits = [.. changes.Select(Stated)];
        }
        catch (ArgumentException)
        {
            // A shape the sentences above do not state yet, which is a rule this boundary has fallen behind on rather
            // than anything the caller can read out of the framework's own wording. The exception carries a parameter
            // name and BCL phrasing, so it is not what an administrative surface answers with.
            return Refusal(
                "One of the changes does not name a setting this deployment can persist. Check that each path is a colon-delimited configuration key and that each value is text the deployment can hold.");
        }

        return TypedResults.Ok(ConfigurationWriteResponse.For(
            await settings.ApplyAsync(edits, request.Version, request.EvenIfShadowed, cancellationToken)));
    }

    /// <summary>Hands over the persisted document itself, as the sparse JSON an editing session opens.</summary>
    /// <param name="settings">The deployment's configuration administration.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>200</c> with the document and the version it was read at, or <c>400</c> when the persisted row is not a document of configuration settings.</returns>
    /// <remarks>
    /// <para>
    /// The document is read from the database rather than from the layer this process composed, because it is what the
    /// caller's save is judged against: composing an edit over what this process happens to hold would author it
    /// against a version another writer may already have replaced.
    /// </para>
    /// <para>
    /// A row nothing here wrote is answered as a refusal rather than left to throw. The column holds any JSON, so a
    /// row edited in the database can be an array, a scalar, or nested past what the parser reads — and an operator
    /// meeting that needs the sentence naming what to correct rather than a failed request and an editing command that
    /// will not open.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ConfigurationDocumentResponse>, ProblemHttpResult>> ReadDocumentAsync(
        [FromServices] PersistedSettingsAdministration settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        PersistedSettingsDocument document;

        try
        {
            document = await settings.ReadDocumentAsync(cancellationToken);
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            // The parser's own message names the offending token, the JSON path it stopped at, and a byte position,
            // and the path is composed from the row's own key names. None of that is this surface's to publish, for
            // the same reason the wording of a rejected change is not.
            return Refusal(
                "The persisted configuration row is not a document of configuration settings, so it cannot be read or edited. Correct the row where it was written.");
        }

        return TypedResults.Ok(new ConfigurationDocumentResponse(document.Version, document.Json));
    }

    /// <summary>Takes back the persisted document an editing session saved.</summary>
    /// <param name="settings">The deployment's configuration administration.</param>
    /// <param name="request">The document and the version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the write did, or <c>400</c> when the request carries no document.</returns>
    internal static async Task<Results<Ok<ConfigurationWriteResponse>, ProblemHttpResult>> SaveDocumentAsync(
        [FromServices] PersistedSettingsAdministration settings,
        [FromBody] ConfigurationDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version < 0)
        {
            return Refusal("A configuration write states the version it was composed over, which is never negative.");
        }

        if (request.Document is not { Length: > 0 } document)
        {
            return Refusal(
                "A saved configuration document carries the document. An editing session that means to change nothing sends nothing at all.");
        }

        return TypedResults.Ok(ConfigurationWriteResponse.For(
            await settings.ApplyDocumentAsync(document, request.Version, request.EvenIfShadowed, cancellationToken)));
    }

    /// <summary>Reports what adopting a path would copy from the deployment's files into the persisted layer.</summary>
    /// <param name="settings">The deployment's configuration administration.</param>
    /// <param name="prefix">The colon-delimited path the adoption would cover.</param>
    /// <returns><c>200</c> with what would be adopted, or <c>400</c> when no prefix was named or it matched too many settings.</returns>
    /// <remarks>
    /// The preview is the same shape as an ordinary reading, and deliberately: what an adoption writes is a set of
    /// settings with a source each, and the source is the part an operator weighs — it names the file that stops
    /// deciding the value once the adoption commits.
    /// </remarks>
    internal static Results<Ok<ConfigurationReadingResponse>, ProblemHttpResult> ReadAdoptable(
        [FromServices] PersistedSettingsAdministration settings,
        [FromQuery] string? prefix)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The same rule the commit guards by, rather than a weaker one. A preview exists to report what the commit
        // would do, so a prefix the commit refuses has to be refused here as well: a preview answering an empty
        // reading for '   ' would tell an operator their files supply nothing beneath a path that is not a path.
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Refusal("An adoption names the path it covers. There is no adoption of the whole configuration.");
        }

        var reading = settings.ReadAdoptable(prefix);

        return reading.IsTooBroad
            ? TooBroad(reading, prefix)
            : TypedResults.Ok(ConfigurationReadingResponse.For(settings.ComposedVersion, reading));
    }

    /// <summary>Copies what the deployment's files supply beneath a path into the persisted layer.</summary>
    /// <param name="settings">The deployment's configuration administration.</param>
    /// <param name="request">The path and the version the preview was read over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>200</c> with what the adoption did, or <c>400</c> when it names no path.</returns>
    internal static async Task<Results<Ok<ConfigurationWriteResponse>, ProblemHttpResult>> AdoptAsync(
        [FromServices] PersistedSettingsAdministration settings,
        [FromBody] ConfigurationAdoptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version < 0)
        {
            return Refusal("A configuration write states the version it was composed over, which is never negative.");
        }

        if (request.Prefix is not { } prefix || string.IsNullOrWhiteSpace(prefix))
        {
            return Refusal("An adoption names the path it covers. There is no adoption of the whole configuration.");
        }

        return TypedResults.Ok(ConfigurationWriteResponse.For(
            await settings.AdoptAsync(prefix, request.Version, request.EvenIfShadowed, cancellationToken)));
    }

    /// <summary>Turns one stated change into the change the writer accepts, refusing a path or value it will not.</summary>
    /// <exception cref="ArgumentException">Thrown when the path names no setting, or either half carries a character no configuration document can hold.</exception>
    private static ConfigurationEdit Stated(ConfigurationChangeRequest change) => change.Value is { } value
        ? ConfigurationEdit.SetTo(change.Path ?? string.Empty, value)
        : ConfigurationEdit.Removing(change.Path ?? string.Empty);

    /// <summary>Names the first path a change states twice, or nothing where each path is stated once.</summary>
    /// <remarks>
    /// A repeated path is refused rather than resolved, because the two layers behind this route disagree about what it
    /// means: <see cref="IConfigurationWriter.WriteAsync" /> applies the edits in the order
    /// given, so the last one would win, while the administration drops an edit that would change nothing about the
    /// document as it currently stands — so <c>A=y</c> followed by <c>A=x</c> over a persisted <c>x</c> keeps the first
    /// and drops the second, committing the value the caller asked to be rid of. Which of the two readings is right is
    /// not a question this boundary should answer on the caller's behalf: a change that names a path twice is a caller
    /// that has not decided what it wants there.
    /// </remarks>
    private static string? NamedTwice(IReadOnlyList<ConfigurationChangeRequest> changes) =>
        changes
            .Select(change => change.Path!)
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(named => named.Count() > 1)
            .Select(named => named.Key)
            .FirstOrDefault();

    /// <summary>Says why one stated change is not a change this boundary accepts, or nothing where it is.</summary>
    /// <remarks>
    /// The same rules <see cref="ConfigurationEdit" /> enforces, stated as sentences an administrator reads. The type
    /// raises each as an argument failure, which is right for a caller that composed the change in process and wrong
    /// for one that arrived over HTTP: the message names a parameter of a constructor and words the bound as the base
    /// class library does, and neither is something this surface publishes. The bounds themselves are read from that
    /// type rather than repeated, so a rule moves in one place.
    /// </remarks>
    private static string? Unacceptable(ConfigurationChangeRequest change)
    {
        if (change.Path is not { Length: > 0 } path || string.IsNullOrWhiteSpace(path))
        {
            return "A configuration change names the path it targets.";
        }

        if (path.Length > ConfigurationEdit.MaximumPathLength)
        {
            return $"A configuration path carries at most {ConfigurationEdit.MaximumPathLength} characters, and this one carries {path.Length}.";
        }

        if (change.Value is { Length: > ConfigurationEdit.MaximumValueLength } value)
        {
            return $"The value for '{path}' carries {value.Length} characters, past the {ConfigurationEdit.MaximumValueLength} a configuration value may hold.";
        }

        return null;
    }

    private static ProblemHttpResult TooBroad(SettingsReading reading, string? prefix) => TypedResults.Problem(
        $"{Named(prefix)} matches {reading.MatchedCount} settings, past the {EffectiveSettingsReader.MaximumSettings} one reading answers with. Read a narrower path.",
        statusCode: StatusCodes.Status400BadRequest);

    private static string Named(string? prefix) =>
        prefix is { Length: > 0 } named ? $"The path '{named}'" : "The whole configuration";

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
