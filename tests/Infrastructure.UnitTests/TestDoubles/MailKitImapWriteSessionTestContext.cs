// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
    /// <returns>The selected folder.</returns>
    internal static IMailFolder CreateWritableFolder(uint uidValidity = 7U)
    {
        var folder = Substitute.For<IMailFolder>();
        folder.IsOpen.Returns(true);
        folder.UidValidity.Returns(uidValidity);
        folder.StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<UniqueId>>([]));
        folder.CopyToAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IMailFolder>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UniqueIdMap.Empty));
        folder.MoveToAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IMailFolder>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UniqueIdMap.Empty));

        return folder;
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
        var pool = new MailboxWriteConnectionPool(
            clientFactory,
            CreateScopeFactory(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            options,
            timeProvider,
            new RecordingCategoryLogger<MailboxWriteConnectionPool>(recordedLogs));
        var telemetry = new MailboxMutationTelemetry(
            new RecordingCategoryLogger<MailboxMutationTelemetry>(recordedLogs),
            new FakeTimeProvider(ObservedAt));

        return new MailboxWriteHarness(pool, telemetry, recordedLogs);
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
    private static IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateSettingsProvider());
        services.AddScoped<IMailAccessTokenSource>(_ => new UnusedMailAccessTokenSource());

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>Adapts the shared recording provider to the typed logger a production type asks for.</summary>
    /// <remarks>
    /// Written out rather than reached through <see cref="LoggerFactory" />, because the framework's
    /// <see cref="Logger{T}" /> takes ownership of a factory that nothing here would ever release.
    /// </remarks>
    private sealed class RecordingCategoryLogger<TCategory> : ILogger<TCategory>
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
