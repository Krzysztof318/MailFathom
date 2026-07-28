// Copyright © 2026 Krzysztof Kasprowicz

using System.Net.Security;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Narrows the IMAP client library to the operations one read-only mailbox session needs.</summary>
/// <remarks>
/// The port exists so a unit test can model a server that drops a connection, refuses a credential, or never answers,
/// without a real socket. Every member maps onto exactly one client operation and adds no behavior of its own.
/// </remarks>
internal interface IMailKitImapClient : IAsyncDisposable
{
    /// <summary>Gets whether the client still holds a usable connection to the server.</summary>
    bool IsConnected { get; }

    /// <summary>Gets the mechanism set the server advertised while connecting, which the caller narrows before authenticating.</summary>
    ISet<string> AuthenticationMechanisms { get; }

    /// <summary>Gets or sets the decision the client asks for when the platform's own certificate validation objects.</summary>
    /// <remarks>
    /// It is left unset for an account that trusts the system store alone, which keeps the client's validating default
    /// in place. Nothing assigned here may accept a certificate the configured policy rejects; it exists to admit a
    /// deployment-provisioned authority, not to forgive an error.
    /// </remarks>
    RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }

    /// <summary>Connects to the server with the configured transport security mode.</summary>
    Task ConnectAsync(
        string host,
        int port,
        SecureSocketOptions options,
        CancellationToken cancellationToken);

    /// <summary>Authenticates the connected client with a mechanism the narrowed advertised set still permits.</summary>
    Task AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);

    /// <summary>Resolves a folder by its remote path without selecting it.</summary>
    Task<IMailFolder> GetFolderAsync(
        string path,
        CancellationToken cancellationToken);

    /// <summary>Gets the namespaces the server assigns to the authenticated user's own folders.</summary>
    IReadOnlyList<FolderNamespace> PersonalNamespaces { get; }

    /// <summary>Gets the mailbox every IMAP server exposes for incoming mail.</summary>
    /// <remarks>Listing a namespace does not always cover it, so discovery reads it separately rather than assuming.</remarks>
    IMailFolder Inbox { get; }

    /// <summary>Lists every folder under one namespace, together with the attributes the server reports for each.</summary>
    /// <remarks>
    /// This is an IMAP <c>LIST</c>, which selects no folder and therefore cannot change a message flag. Nothing in
    /// this port creates, renames, subscribes to, or deletes a folder.
    /// </remarks>
    Task<IReadOnlyList<IMailFolder>> GetFoldersAsync(
        FolderNamespace folderNamespace,
        CancellationToken cancellationToken);

    /// <summary>Closes the connection, optionally sending the protocol's logout command first.</summary>
    Task DisconnectAsync(
        bool quit,
        CancellationToken cancellationToken);
}

/// <summary>Adapts the MailKit client onto the narrowed port.</summary>
[RequiresIntegrationCoverage]
internal sealed class MailKitImapClientAdapter(ImapClient client) : IMailKitImapClient
{
    /// <inheritdoc />
    public bool IsConnected => client.IsConnected;

    /// <inheritdoc />
    public ISet<string> AuthenticationMechanisms => client.AuthenticationMechanisms;

    /// <inheritdoc />
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback
    {
        get => client.ServerCertificateValidationCallback;
        set => client.ServerCertificateValidationCallback = value;
    }

    /// <inheritdoc />
    public Task ConnectAsync(
        string host,
        int port,
        SecureSocketOptions options,
        CancellationToken cancellationToken) => client.ConnectAsync(host, port, options, cancellationToken);

    /// <inheritdoc />
    public Task AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken) => client.AuthenticateAsync(userName, password, cancellationToken);

    /// <inheritdoc />
    public Task<IMailFolder> GetFolderAsync(
        string path,
        CancellationToken cancellationToken) => client.GetFolderAsync(path, cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<FolderNamespace> PersonalNamespaces => [.. client.PersonalNamespaces];

    /// <inheritdoc />
    public IMailFolder Inbox => client.Inbox;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IMailFolder>> GetFoldersAsync(
        FolderNamespace folderNamespace,
        CancellationToken cancellationToken) =>
        [.. await client.GetFoldersAsync(folderNamespace, subscribedOnly: false, cancellationToken)];

    /// <inheritdoc />
    public Task DisconnectAsync(
        bool quit,
        CancellationToken cancellationToken) => client.DisconnectAsync(quit, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        client.Dispose();

        return ValueTask.CompletedTask;
    }
}
