// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using NSubstitute;
using NSubstitute.Core;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Scripts one IMAP connection to a server a test describes, including the ways that server misbehaves.</summary>
/// <remarks>
/// <para>
/// The client is a substitute of MailKit's own <see cref="IImapClient" /> rather than an implementation of a port
/// this repository declared: the library publishes the interface, so restating it here would leave a copy to go stale
/// the moment MailKit moves. What this type adds is the part a substitute cannot express on its own — connection
/// state that changes as commands run, so a dropped socket is observable exactly as the adapter would observe it.
/// </para>
/// <para>
/// A client is single-use, exactly as the real one is: the adapter creates one per establishment attempt and disposes
/// it when the connection ends, so a test that expects a reconnection hands the factory a second instance.
/// </para>
/// </remarks>
internal sealed class FakeImapClient
{
    private readonly HashSet<string> authenticationMechanisms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FolderNamespace, IReadOnlyList<IMailFolder>> foldersByNamespace = [];

    private bool isConnected;
    private RemoteCertificateValidationCallback? serverCertificateValidationCallback;

    internal FakeImapClient()
    {
        this.Client = Substitute.For<IImapClient>();

        this.ScriptCertificateValidation();
        this.ScriptConnect();
        this.ScriptAuthenticate();
        this.ScriptFolderAccess();
        this.ScriptNamespaces();
        this.ScriptIdle();
        this.ScriptDisconnectAndDispose();
    }

    /// <summary>Gets the client the adapter under test is handed.</summary>
    internal IImapClient Client { get; }

    /// <summary>Gets the mechanism set the server advertises, which the adapter narrows before authenticating.</summary>
    internal ISet<string> AuthenticationMechanisms => this.authenticationMechanisms;

    /// <summary>Gets the SASL mechanism name of every token authentication attempted, in order.</summary>
    internal List<string> SaslMechanismNames { get; } = [];

    /// <summary>Gets the access token presented by every token authentication attempted, in order.</summary>
    /// <remarks>Recorded so a test can prove that a second attempt presented the renewed token rather than repeating the refused one.</remarks>
    internal List<string> PresentedAccessTokens { get; } = [];

    /// <summary>Gets or sets how many of the first token authentications the server refuses.</summary>
    /// <remarks>Models a token this process still believes is valid being rejected — revoked, or the mailbox password changed — which is the one case no expiry instant predicts.</remarks>
    internal int RefusedSaslAuthenticationCount { get; set; }

    /// <summary>Gets the folders each namespace advertises, so a test can model a server with several of them.</summary>
    internal IDictionary<FolderNamespace, IReadOnlyList<IMailFolder>> FoldersByNamespace => this.foldersByNamespace;

    internal IReadOnlyList<FolderNamespace> PersonalNamespaces { get; set; } = [new FolderNamespace('/', string.Empty)];

    internal IReadOnlyList<FolderNamespace> OtherNamespaces { get; set; } = [];

    internal IReadOnlyList<FolderNamespace> SharedNamespaces { get; set; } = [];

    /// <summary>Gets or sets the folder the server answers a selection with.</summary>
    internal IMailFolder? Folder { get; set; }

    /// <summary>Gets or sets the folder the server answers as its inbox.</summary>
    internal IMailFolder? InboxFolder { get; set; }

    internal IReadOnlyList<string> MechanismsWhenAuthenticated { get; private set; } = [];

    internal bool AuthenticateCalled { get; private set; }

    internal int ConnectCount { get; private set; }

    internal int DisconnectCount { get; private set; }

    internal int DisposeCount { get; private set; }

    internal int GetFolderAsyncCount { get; private set; }

    internal int GetFoldersAsyncCount { get; private set; }

    internal List<string> RequestedFolderPaths { get; } = [];

    internal SecureSocketOptions? ConnectSocketOptions { get; private set; }

    internal RemoteCertificateValidationCallback? ValidationCallbackWhenConnected { get; private set; }

    internal Exception? ConnectException { get; set; }

    /// <summary>Replaces the connect step, so a test can model a server that accepts the socket and then never answers.</summary>
    internal Func<CancellationToken, Task>? ConnectBehavior { get; set; }

    internal Exception? AuthenticateException { get; set; }

    internal Exception? DisconnectException { get; set; }

    internal Exception? DisposeException { get; set; }

    internal Exception? GetFoldersException { get; set; }

    /// <summary>Gets or sets the capabilities the server advertises once the connection is established.</summary>
    /// <remarks>Defaults to none, so a test that wants push has to say so and one that does not is modelling the ordinary server this adapter still has to work against.</remarks>
    internal ImapCapabilities Capabilities { get; set; } = ImapCapabilities.None;

    /// <summary>Gets how many IDLE commands the adapter has issued, which is how a renewal is counted.</summary>
    internal int IdleCount { get; private set; }

    /// <summary>Gets a signal that completes once the adapter is inside an IDLE command, so a test can act while it waits.</summary>
    internal TaskCompletionSource IdleEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets or sets what the server does while the client is idling, such as reporting that the folder changed.</summary>
    internal Func<CancellationToken, Task>? IdleBehavior { get; set; }

    /// <summary>Gets or sets the failure an IDLE command ends with, which models a connection lost while nothing was being asked of it.</summary>
    internal Exception? IdleException { get; set; }

    /// <summary>Models the server closing the connection while the current command is in flight.</summary>
    internal Exception DropConnection(Exception failure)
    {
        this.isConnected = false;

        return failure;
    }

    /// <summary>Backs the callback property with a field, because an unset delegate member of a substitute answers with a substitute rather than with null.</summary>
    /// <remarks>
    /// The distinction is the assertion of one test: an account trusting the system store must leave the client's own
    /// validating default in place, which is only observable as the callback still being unset.
    /// </remarks>
    private void ScriptCertificateValidation()
    {
        this.Client.ServerCertificateValidationCallback.Returns(_ => this.serverCertificateValidationCallback);
        this.Client
            .When(client => client.ServerCertificateValidationCallback = Arg.Any<RemoteCertificateValidationCallback?>())
            .Do(call => this.serverCertificateValidationCallback = call.Arg<RemoteCertificateValidationCallback?>());
    }

    private void ScriptConnect() =>
        this.Client
            .ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => this.ConnectAsync(call));

    private async Task ConnectAsync(CallInfo call)
    {
        this.ConnectCount++;
        this.ValidationCallbackWhenConnected = this.serverCertificateValidationCallback;
        this.ConnectSocketOptions = call.Arg<SecureSocketOptions>();

        if (this.ConnectException is not null)
        {
            throw this.ConnectException;
        }

        if (this.ConnectBehavior is not null)
        {
            await this.ConnectBehavior(call.Arg<CancellationToken>());
        }

        this.isConnected = true;
    }

    private void ScriptAuthenticate()
    {
        this.Client.IsConnected.Returns(_ => this.isConnected);
        this.Client.AuthenticationMechanisms.Returns(_ => this.authenticationMechanisms);
        this.Client
            .AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                this.AuthenticateCalled = true;
                this.MechanismsWhenAuthenticated = [.. this.authenticationMechanisms.Order(StringComparer.Ordinal)];

                return this.AuthenticateException is null
                    ? Task.CompletedTask
                    : throw this.AuthenticateException;
            });

        this.Client
            .AuthenticateAsync(Arg.Any<SaslMechanism>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var mechanism = call.Arg<SaslMechanism>()
                    ?? throw new InvalidOperationException("The adapter authenticated with no SASL mechanism.");

                this.AuthenticateCalled = true;
                this.MechanismsWhenAuthenticated = [.. this.authenticationMechanisms.Order(StringComparer.Ordinal)];
                this.SaslMechanismNames.Add(mechanism.MechanismName);
                this.PresentedAccessTokens.Add(mechanism.Credentials?.Password ?? string.Empty);

                // Counted against the attempts already recorded, so "refuse the first one" reads as exactly that.
                return this.SaslMechanismNames.Count <= this.RefusedSaslAuthenticationCount
                    ? throw new AuthenticationException("The server refused the access token.")
                    : Task.CompletedTask;
            });
    }

    private void ScriptFolderAccess()
    {
        this.Client.Inbox.Returns(_ =>
            this.InboxFolder ?? throw new InvalidOperationException("No test inbox configured."));
        this.Client
            .GetFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                this.GetFolderAsyncCount++;
                this.RequestedFolderPaths.Add(call.Arg<string>()!);

                return Task.FromResult(
                    this.Folder ?? throw new InvalidOperationException("No test folder configured."));
            });
    }

    private void ScriptNamespaces()
    {
        this.Client.PersonalNamespaces.Returns(_ => ToNamespaceCollection(this.PersonalNamespaces));
        this.Client.OtherNamespaces.Returns(_ => ToNamespaceCollection(this.OtherNamespaces));
        this.Client.SharedNamespaces.Returns(_ => ToNamespaceCollection(this.SharedNamespaces));
        this.Client
            .GetFoldersAsync(Arg.Any<FolderNamespace>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                this.GetFoldersAsyncCount++;

                if (this.GetFoldersException is not null)
                {
                    throw this.GetFoldersException;
                }

                IList<IMailFolder> folders = this.foldersByNamespace.TryGetValue(call.Arg<FolderNamespace>()!, out var advertised)
                    ? [.. advertised]
                    : [];

                return Task.FromResult(folders);
            });
    }

    /// <summary>Models the IDLE command: it holds until the done token ends the idle state, exactly as MailKit's does.</summary>
    /// <remarks>
    /// Returning normally on the done token is the whole contract the adapter is written against — cancelling it is how
    /// a client leaves IDLE, not how it fails — so a fake that threw there would let the adapter pass a test it could
    /// never pass against the library.
    /// </remarks>
    private void ScriptIdle()
    {
        this.Client.Capabilities.Returns(_ => this.Capabilities);
        this.Client
            .IdleAsync(Arg.Any<CancellationToken>(), Arg.Any<CancellationToken>())
            .Returns(call => this.IdleAsync(call.ArgAt<CancellationToken>(0)));
    }

    private async Task IdleAsync(CancellationToken doneToken)
    {
        this.IdleCount++;
        this.IdleEntered.TrySetResult();

        if (this.IdleException is not null)
        {
            throw this.DropConnection(this.IdleException);
        }

        if (this.IdleBehavior is not null)
        {
            await this.IdleBehavior(doneToken);
        }

        await WaitUntilIdleStateEndsAsync(doneToken);
    }

    private static async Task WaitUntilIdleStateEndsAsync(CancellationToken doneToken)
    {
        if (doneToken.IsCancellationRequested)
        {
            return;
        }

        var idleStateEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = doneToken.Register(() => idleStateEnded.TrySetResult());

        await idleStateEnded.Task;
    }

    private void ScriptDisconnectAndDispose()
    {
        this.Client
            .DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                this.DisconnectCount++;
                if (this.DisconnectException is not null)
                {
                    throw this.DisconnectException;
                }

                this.isConnected = false;

                return Task.CompletedTask;
            });

        this.Client.When(client => client.Dispose()).Do(_ =>
        {
            this.DisposeCount++;
            if (this.DisposeException is not null)
            {
                throw this.DisposeException;
            }
        });
    }

    private static FolderNamespaceCollection ToNamespaceCollection(IReadOnlyList<FolderNamespace> namespaces)
    {
        var collection = new FolderNamespaceCollection();
        foreach (var folderNamespace in namespaces)
        {
            collection.Add(folderNamespace);
        }

        return collection;
    }
}
