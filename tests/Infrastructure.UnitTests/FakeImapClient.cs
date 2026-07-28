// Copyright © 2026 Krzysztof Kasprowicz

using System.Net.Security;
using MailKit;
using MailKit.Security;
using MailMcp.Infrastructure.Mail.MailKit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Models one IMAP connection to a server a test scripts, including the ways that server misbehaves.</summary>
/// <remarks>
/// A client is single-use, exactly as the real one is: the adapter creates one per establishment attempt and disposes
/// it when the connection ends, so a test that expects a reconnection hands the factory a second instance.
/// </remarks>
internal sealed class FakeImapClient : IMailKitImapClient
{
    public bool IsConnected { get; set; }

    public ISet<string> AuthenticationMechanisms { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }

    public IReadOnlyList<string> MechanismsWhenAuthenticated { get; private set; } = [];

    public bool AuthenticateCalled { get; private set; }

    public IMailFolder? Folder { get; set; }

    public int ConnectCount { get; private set; }

    public int DisconnectCount { get; private set; }

    public int DisposeCount { get; private set; }

    public int GetFolderAsyncCount { get; private set; }

    public Exception? ConnectException { get; set; }

    /// <summary>Replaces the connect step, so a test can model a server that accepts the socket and then never answers.</summary>
    public Func<CancellationToken, Task>? ConnectBehavior { get; set; }

    public Exception? AuthenticateException { get; set; }

    public Exception? DisconnectException { get; set; }

    public Exception? DisposeException { get; set; }

    public SecureSocketOptions? ConnectSocketOptions { get; private set; }

    public RemoteCertificateValidationCallback? ValidationCallbackWhenConnected { get; private set; }

    public async Task ConnectAsync(
        string host,
        int port,
        SecureSocketOptions options,
        CancellationToken cancellationToken)
    {
        this.ConnectCount++;
        this.ValidationCallbackWhenConnected = this.ServerCertificateValidationCallback;
        this.ConnectSocketOptions = options;

        if (this.ConnectException is not null)
        {
            throw this.ConnectException;
        }

        if (this.ConnectBehavior is not null)
        {
            await this.ConnectBehavior(cancellationToken);
        }

        this.IsConnected = true;
    }

    public Task AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        this.AuthenticateCalled = true;
        this.MechanismsWhenAuthenticated = [.. this.AuthenticationMechanisms.Order(StringComparer.Ordinal)];

        if (this.AuthenticateException is not null)
        {
            throw this.AuthenticateException;
        }

        return Task.CompletedTask;
    }

    public Task<IMailFolder> GetFolderAsync(
        string path,
        CancellationToken cancellationToken)
    {
        this.GetFolderAsyncCount++;
        this.RequestedFolderPaths.Add(path);

        return Task.FromResult(this.Folder ?? throw new InvalidOperationException("No test folder configured."));
    }

    public IReadOnlyList<FolderNamespace> PersonalNamespaces { get; set; } = [new FolderNamespace('/', string.Empty)];

    public IMailFolder? InboxFolder { get; set; }

    public IMailFolder Inbox =>
        this.InboxFolder ?? throw new InvalidOperationException("No test inbox configured.");

    /// <summary>Gets the folders each listed namespace advertises, so a test can model a server with several of them.</summary>
    public Dictionary<FolderNamespace, IReadOnlyList<IMailFolder>> FoldersByNamespace { get; } = [];

    public Exception? GetFoldersException { get; set; }

    public int GetFoldersAsyncCount { get; private set; }

    public List<string> RequestedFolderPaths { get; } = [];

    public Task<IReadOnlyList<IMailFolder>> GetFoldersAsync(
        FolderNamespace folderNamespace,
        CancellationToken cancellationToken)
    {
        this.GetFoldersAsyncCount++;

        if (this.GetFoldersException is not null)
        {
            throw this.GetFoldersException;
        }

        return Task.FromResult(
            this.FoldersByNamespace.TryGetValue(folderNamespace, out var folders) ? folders : []);
    }

    public Task DisconnectAsync(
        bool quit,
        CancellationToken cancellationToken)
    {
        this.DisconnectCount++;
        if (this.DisconnectException is not null)
        {
            throw this.DisconnectException;
        }

        this.IsConnected = false;

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        this.DisposeCount++;
        if (this.DisposeException is not null)
        {
            throw this.DisposeException;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Models the server closing the connection while the current command is in flight.</summary>
    internal Exception DropConnection(Exception failure)
    {
        this.IsConnected = false;

        return failure;
    }
}
