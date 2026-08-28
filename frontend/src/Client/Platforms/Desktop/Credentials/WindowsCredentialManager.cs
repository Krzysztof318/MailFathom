// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MailFathom.Client.Platforms.Desktop.Credentials;

/// <summary>Holds the sign-in in the Windows Credential Manager.</summary>
/// <remarks>
/// <para>
/// One generic credential, persisted for the logged-on user on this computer. Windows protects it under that user's
/// profile, which is the property a file beside the application could not have: another account on the same machine
/// cannot read it, and a copy of the profile directory taken elsewhere does not open.
/// </para>
/// <para>
/// The Data Protection API at <c>CurrentUser</c> scope is deliberately not taken instead. It protects a value to the
/// same user account but stores nothing, so taking it would leave this choosing where the ciphertext is written — and
/// the two places nearest to hand are the two
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// refuses. The Credential Manager answers where as well as how.
/// </para>
/// <para>
/// Reached by <c>advapi32</c> directly rather than through a package. There is no managed API for the Credential
/// Manager in the base class library and the four entry points this needs are stable back to Windows XP, so a package
/// would be a supply-chain and licensing decision under ADR 0016 bought for four declarations. The imports are
/// source-generated, which is what keeps them through a trimmed publish.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsCredentialManager : IDesktopSecretStore
{
    /// <summary>The credential type that belongs to no authentication package, which is what an application's own secret is.</summary>
    private const uint GenericCredential = 1;

    /// <summary>Kept for every later logon session of this user on this computer, and visible to no other computer.</summary>
    private const uint PersistLocalMachine = 2;

    /// <summary><c>ERROR_NOT_FOUND</c>, which is the ordinary answer for a head that has stored nothing yet.</summary>
    private const int NotFound = 1168;

    /// <summary><c>CRED_MAX_CREDENTIAL_BLOB_SIZE</c>, which the Credential Manager refuses to exceed.</summary>
    private const int MaximumBlobSize = 5 * 512;

    /// <summary>How the one entry is named, which is the whole of how it is found again.</summary>
    /// <remarks>
    /// Prefixed with the product and the head, as the platform asks a generic credential to be, so somebody reading the
    /// Credential Manager sees which application wrote it and can remove it from there. Nothing further is in the name:
    /// there is one entry, and putting the deployment address into the name would publish which server somebody uses
    /// to anything that can list credential names without protecting anything.
    /// </remarks>
    private const string EntryName = "MailFathom/client/sign-in";

    /// <inheritdoc />
    /// <remarks>The Credential Manager is part of every Windows installation this head runs on, so there is nothing to probe for.</remarks>
    public bool IsReachable => true;

    /// <inheritdoc />
    public string? Read()
    {
        if (!CredRead(EntryName, GenericCredential, 0, out var held))
        {
            var error = Marshal.GetLastPInvokeError();

            return error == NotFound ? null : throw Refused("read", error);
        }

        try
        {
            return ReadBlob(held);
        }
        finally
        {
            CredFree(held);
        }
    }

    /// <inheritdoc />
    public void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var blob = Encoding.UTF8.GetBytes(value);

        if (blob.Length > MaximumBlobSize)
        {
            throw new DesktopSecretStoreUnavailable(
                $"the Credential Manager holds at most {MaximumBlobSize} bytes per entry and this sign-in is {blob.Length}");
        }

        if (!TryWrite(blob))
        {
            throw Refused("write", Marshal.GetLastPInvokeError());
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (CredDelete(EntryName, GenericCredential, 0))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();

        if (error != NotFound)
        {
            throw Refused("remove", error);
        }
    }

    private static unsafe string ReadBlob(nint held)
    {
        var credential = *(Credential*)held;

        return credential.CredentialBlob == 0
            ? string.Empty
            : Encoding.UTF8.GetString((byte*)credential.CredentialBlob, (int)credential.CredentialBlobSize);
    }

    /// <summary>Fills in the structure the platform writes from, with the two strings and the blob pinned for the call.</summary>
    /// <remarks>
    /// <c>UserName</c> is what the Credential Manager's own listing shows beside an entry, and the platform ignores it
    /// for a generic credential — so it names the application rather than the owner whose sign-in this holds, which
    /// would publish a username to anything that can list credentials.
    /// </remarks>
    private static unsafe bool TryWrite(byte[] blob)
    {
        fixed (char* target = EntryName)
        fixed (char* user = "MailFathom")
        fixed (byte* contents = blob)
        {
            Credential credential = new()
            {
                Type = GenericCredential,
                TargetName = (nint)target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = (nint)contents,
                Persist = PersistLocalMachine,
                UserName = (nint)user,
            };

            return CredWrite(&credential, 0);
        }
    }

    /// <summary>Turns a Windows error into the sentence a diagnostic reads before the head falls back to memory.</summary>
    private static DesktopSecretStoreUnavailable Refused(string attempt, int error)
    {
        Win32Exception cause = new(error);

        return new DesktopSecretStoreUnavailable(
            string.Create(
                CultureInfo.InvariantCulture,
                $"the Windows Credential Manager refused to {attempt} this sign-in ({cause.Message.TrimEnd('.')})"),
            cause);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string targetName, uint type, uint flags, out nint credential);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CredWrite(Credential* credential, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string targetName, uint type, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);

    /// <summary><c>CREDENTIALW</c>, in the field order the platform header declares.</summary>
    /// <remarks>
    /// Every member is present even though this writes five of them, because the layout is what the call reads and a
    /// shortened structure would be read past its end. <c>LastWritten</c> is a <c>FILETIME</c>, which is two
    /// <c>DWORD</c>s and therefore eight bytes whatever the architecture.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }
}
