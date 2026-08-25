// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MailFathom.Cli.Credentials.SecretStores;

/// <summary>Holds a profile's secrets in the Windows Credential Manager.</summary>
/// <remarks>
/// <para>
/// A generic credential per secret, persisted for the logged-on user on this computer. Windows protects it under that
/// user's profile, which is the property the credentials file could not have: another account on the same machine
/// cannot read it, and a copy of the profile directory taken elsewhere does not open.
/// </para>
/// <para>
/// Reached by <c>advapi32</c> directly rather than through a package. There is no managed API for the Credential
/// Manager in the base class library, and the three entry points this needs are stable back to Windows XP — so a
/// package would be a supply-chain decision bought for four declarations. The imports are source-generated, so they
/// survive the trimmed single-file publish the release workflow produces.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsCredentialManager : IOperatorSecretStore
{
    /// <summary>The credential type that belongs to no authentication package, which is what an application's own secret is.</summary>
    private const uint GenericCredential = 1;

    /// <summary>Kept for every later logon session of this user on this computer, and visible to no other computer.</summary>
    private const uint PersistLocalMachine = 2;

    /// <summary><c>ERROR_NOT_FOUND</c>, which is the ordinary answer for a profile that has stored nothing yet.</summary>
    private const int NotFound = 1168;

    /// <summary><c>CRED_MAX_CREDENTIAL_BLOB_SIZE</c>, which the Credential Manager refuses to exceed.</summary>
    private const int MaximumBlobSize = 5 * 512;

    /// <inheritdoc />
    public string Description => "the Windows Credential Manager";

    /// <inheritdoc />
    public string? Read(ProfileSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (!CredRead(TargetNameOf(secret), GenericCredential, 0, out var held))
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
    public void Write(ProfileSecret secret, string value)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(value);

        var blob = Encoding.UTF8.GetBytes(value);

        if (blob.Length > MaximumBlobSize)
        {
            throw new SecretStoreUnavailable(
                $"the Credential Manager holds at most {MaximumBlobSize} bytes per entry and this credential is {blob.Length}");
        }

        if (!TryWrite(TargetNameOf(secret), blob))
        {
            throw Refused("write", Marshal.GetLastPInvokeError());
        }
    }

    /// <inheritdoc />
    public bool Clear(ProfileSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (CredDelete(TargetNameOf(secret), GenericCredential, 0))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();

        return error == NotFound ? false : throw Refused("remove", error);
    }

    /// <summary>Names one entry the way the Credential Manager lists it.</summary>
    /// <remarks>
    /// Prefixed with the product and the command, as the platform asks a generic credential to be, so an operator
    /// reading the Credential Manager sees which application wrote it and can remove it from there. The address, the
    /// profile, and the kind follow, which is the key this store is addressed by; the address leads so that one
    /// deployment's entries list together.
    /// </remarks>
    private static string TargetNameOf(ProfileSecret secret) =>
        $"MailFathom/mfctl/{secret.Address}/{secret.Profile}/{secret.Kind}";

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
    /// for a generic credential — so it names the command rather than an account that does not exist here.
    /// </remarks>
    private static unsafe bool TryWrite(string targetName, byte[] blob)
    {
        fixed (char* target = targetName)
        fixed (char* user = "mfctl")
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

    /// <summary>Turns a Windows error into the sentence an operator reads before the command falls back to the file.</summary>
    private static SecretStoreUnavailable Refused(string attempt, int error)
    {
        Win32Exception cause = new(error);

        return new SecretStoreUnavailable(
            string.Create(
                CultureInfo.InvariantCulture,
                $"the Windows Credential Manager refused to {attempt} this credential ({cause.Message.TrimEnd('.')})"),
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
    /// Every member is present even though this writes four of them, because the layout is what the call reads and a
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
