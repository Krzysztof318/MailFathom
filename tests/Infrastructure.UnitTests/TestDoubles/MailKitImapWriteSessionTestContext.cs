// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Builds the harness, the folders, and the server answers a write-session test arranges around.</summary>
/// <remarks>
/// The scope factory is a real one over a container holding only the two scoped collaborators a write connection
/// resolves, because owning that scope is part of the pool's contract: a substitute would let a test pass while the
/// production wiring resolved nothing.
/// </remarks>
internal static class MailKitImapWriteSessionTestContext
{
    /// <summary>The remote path every relocation and copy in these tests names as its destination.</summary>
    internal const string ArchivePath = "Archive";

    internal static MailFolderResolution ArchiveFolder { get; } = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("archive"),
        RemoteFolderPath.Create(ArchivePath, '/'));

    /// <summary>A second folder of the same account, for the tests about what a second selection does to the connection.</summary>
    /// <remarks>
    /// Its path is deliberately not <see cref="ArchivePath" />. That one already resolves to the unopened destination
    /// stub a relocation copies into, and selecting it would be selecting a folder no server would have answered a
    /// selection with.
    /// </remarks>
    internal static MailFolderResolution DraftsFolder { get; } = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("drafts"),
        RemoteFolderPath.Create("Drafts", '/'));

    /// <summary>Builds an occurrence identity scoped to one folder binding, which is what a write session accepts.</summary>
    internal static EmailOccurrenceId CreateOccurrenceIn(MailFolderResolution folder, uint uid, uint uidValidity = 7U) =>
        EmailOccurrenceId.Create(PrimaryAccount, folder.Id, ImapUidValidity.Create(uidValidity), ImapUid.Create(uid));

    /// <summary>Builds a folder a server answers a successful selection with, ready to answer mutation commands.</summary>
    /// <param name="uidValidity">The UIDVALIDITY the folder reports.</param>
    /// <param name="keepsAnyKeyword">Whether the folder answers <c>PERMANENTFLAGS</c> with <c>\*</c>, which most do.</param>
    /// <param name="keptKeywords">The keywords the folder keeps by name, for a folder that does not accept new ones.</param>
    /// <returns>The selected folder.</returns>
    internal static IMailFolder CreateWritableFolder(
        uint uidValidity = 7U,
        bool keepsAnyKeyword = true,
        params string[] keptKeywords)
    {
        var folder = Substitute.For<IMailFolder>();
        folder.IsOpen.Returns(true);
        folder.UidValidity.Returns(uidValidity);
        folder.PermanentFlags.Returns(keepsAnyKeyword ? MessageFlags.UserDefined : MessageFlags.None);
        folder.PermanentKeywords.Returns(new HashSet<string>(keptKeywords, StringComparer.OrdinalIgnoreCase));
        folder.StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<UniqueId>>([]));
        folder.CopyToAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IMailFolder>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UniqueIdMap.Empty));
        folder.MoveToAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IMailFolder>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UniqueIdMap.Empty));
        AnswerWithCarriedKeywords(folder);

        return folder;
    }

    /// <summary>Answers the flag fetch a keyword replacement reads the message's current keywords from.</summary>
    /// <param name="openFolder">The selected folder the mutation runs against.</param>
    /// <param name="carried">The keywords the message currently carries, as the server would report them.</param>
    /// <remarks>
    /// The summary carries <see cref="IMessageSummary.Keywords" /> because that is where MailKit puts the keyword half
    /// of one <c>FLAGS</c> answer. A double answering through <see cref="IMessageSummary.Flags" /> instead would let a
    /// replacement reading the right property pass here and remove nothing against a real server.
    /// </remarks>
    internal static void AnswerWithCarriedKeywords(IMailFolder openFolder, params string[] carried)
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.Keywords.Returns(new HashSet<string>(carried, StringComparer.OrdinalIgnoreCase));

        openFolder
            .FetchAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IFetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<IMessageSummary>>([summary]));
    }

    /// <summary>Answers the copy and the move with the identity a <c>COPYUID</c> response would have named.</summary>
    /// <param name="openFolder">The selected folder the mutation runs against.</param>
    /// <param name="sourceUid">The UID the message had in the source folder.</param>
    /// <param name="destinationUid">The UID the destination folder assigned it.</param>
    /// <param name="destinationUidValidity">The UIDVALIDITY the response names beside that UID.</param>
    /// <remarks>
    /// The validity travels on the returned <see cref="UniqueId" /> rather than on the destination folder, because that
    /// is where RFC 4315 puts it and where MailKit surfaces it: a destination folder resolved by path is never selected,
    /// so its own <c>UidValidity</c> is zero. A double that put the value on the folder instead would let a session
    /// reading it there pass here and throw against a real server.
    /// </remarks>
    internal static void AnswerWithCopyUid(
        IMailFolder openFolder,
        uint sourceUid,
        uint destinationUid,
        uint destinationUidValidity = 11U)
    {
        var copyUidMap = new UniqueIdMap(
            [new UniqueId(sourceUid)],
            [new UniqueId(destinationUidValidity, destinationUid)]);

        openFolder.CopyToAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IMailFolder>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(copyUidMap));
        openFolder.MoveToAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IMailFolder>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(copyUidMap));
    }

    /// <summary>Builds a harness over one scripted connection whose server advertises what the test configured.</summary>
    internal static MailboxWriteHarness CreateHarness(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        IMailFolder openFolder)
    {
        var preparedClient = PrepareServer(client, openFolder);

        return CreateHarness(
            resilience,
            () => preparedClient.Client,
            new FakeTimeProvider(ObservedAt),
            new MailboxWriteSessionOptions());
    }

    /// <summary>Builds a harness over a scripted connection sequence, so an idle expiry can be asserted by reconnection.</summary>
    internal static MailboxWriteHarness CreateHarness(
        OutboundResilienceTestHost resilience,
        Func<IImapClient> clientFactory,
        TimeProvider timeProvider,
        MailboxWriteSessionOptions options)
    {
        var recordedLogs = new RecordingLoggerProvider();
        var scopeDisposals = new ScopeDisposalCounter();
        var pool = new MailboxWriteConnectionPool(
            clientFactory,
            CreateScopeFactory(scopeDisposals),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            options,
            timeProvider,
            new RecordingCategoryLogger<MailboxWriteConnectionPool>(recordedLogs));
        var telemetry = new MailboxMutationTelemetry(
            new RecordingCategoryLogger<MailboxMutationTelemetry>(recordedLogs),
            new FakeTimeProvider(ObservedAt));

        return new MailboxWriteHarness(pool, telemetry, recordedLogs, scopeDisposals);
    }

    /// <summary>Prepares one scripted connection to answer a selection with the folder and a destination lookup by path.</summary>
    internal static FakeImapClient PrepareServer(FakeImapClient client, IMailFolder openFolder)
    {
        client.Folder = openFolder;
        client.AuthenticationMechanisms.Add("PLAIN");
        client.FoldersByPath[ArchivePath] = CreateDestinationFolder();

        return client;
    }

    /// <summary>Builds the destination folder a relocation or a copy names, which the server resolves by path.</summary>
    /// <remarks>
    /// Its <c>UidValidity</c> is left at the zero an unopened folder reports, which is what a server actually hands
    /// back for a folder resolved by path and never selected. Nothing may read the destination's identity from here.
    /// </remarks>
    private static IMailFolder CreateDestinationFolder()
    {
        var destination = Substitute.For<IMailFolder>();
        destination.FullName.Returns(ArchivePath);

        return destination;
    }

    /// <summary>A container holding exactly the two scoped services a write connection resolves from its own scope.</summary>
    /// <param name="scopeDisposals">Counts the scopes the pool released, so a test can prove one was not leaked.</param>
    /// <remarks>
    /// The probe is resolved by the settings provider's own factory rather than registered and forgotten, because a
    /// scoped service nothing ever asks for is never constructed and therefore never disposed — a probe like that would
    /// report zero disposals whatever the pool did.
    /// </remarks>
    private static IServiceScopeFactory CreateScopeFactory(ScopeDisposalCounter scopeDisposals)
    {
        var services = new ServiceCollection();
        services.AddSingleton(scopeDisposals);
        services.AddScoped<ScopeDisposalProbe>();
        services.AddScoped(provider =>
        {
            provider.GetRequiredService<ScopeDisposalProbe>();

            return CreateSettingsProvider();
        });
        services.AddScoped<IMailAccessTokenSource>(_ => new UnusedMailAccessTokenSource());

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>Counts how many of the pool's per-connection scopes were disposed.</summary>
    internal sealed class ScopeDisposalCounter
    {
        internal int Count { get; private set; }

        internal void RecordDisposal() => this.Count++;
    }

    /// <summary>A scoped service whose disposal is the observable fact that the scope around it was released.</summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The container instantiates it per scope; the settings provider's factory resolves it so the scope actually constructs one.")]
    private sealed class ScopeDisposalProbe(ScopeDisposalCounter counter) : IDisposable
    {
        public void Dispose() => counter.RecordDisposal();
    }

    /// <summary>Adapts the shared recording provider to the typed logger a production type asks for.</summary>
    /// <remarks>
    /// Written out rather than reached through <see cref="LoggerFactory" />, because the framework's
    /// <see cref="Logger{T}" /> takes ownership of a factory that nothing here would ever release.
    /// </remarks>
    internal sealed class RecordingCategoryLogger<TCategory> : ILogger<TCategory>
    {
        private readonly ILogger inner;

        internal RecordingCategoryLogger(RecordingLoggerProvider provider) =>
            this.inner = provider.CreateLogger(typeof(TCategory).FullName ?? typeof(TCategory).Name);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => this.inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => this.inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
