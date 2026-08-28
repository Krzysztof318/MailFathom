// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MailFathom.Client.Platforms.Desktop.Credentials;

/// <summary>Holds the sign-in in the login keychain, through Keychain Services.</summary>
/// <remarks>
/// <para>
/// A generic password item, scoped to this application and not synchronized to iCloud. macOS protects it under the
/// signed-in user's login keychain, which is the property a file beside the application could not have: another
/// account on the same Mac cannot read it, and a copy of the home directory taken elsewhere does not open.
/// </para>
/// <para>
/// Reached through <c>SecItem</c> — the modern API — rather than the deprecated <c>SecKeychain</c> family, and by
/// P/Invoke rather than through a package: there is no managed API for either, and a package would be a supply-chain
/// and licensing decision under ADR 0016 bought for a handful of declarations. The item is described by a
/// <c>CFDictionary</c>, so a small amount of Core Foundation is unavoidable; every handle this creates is released on
/// the way out, which is what the <see cref="CoreFoundationScope" /> below is for.
/// </para>
/// <para>
/// <c>kSecAttrSynchronizable</c> is left at its default, which is not synchronizable. A password synchronized to iCloud
/// would leave the boundary
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// draws — one operating-system user on one machine — for one nobody stated.
/// </para>
/// <para>
/// Nothing here is exercised by this repository's tests and nothing could be: the call needs a macOS login session, so
/// review of this file is what checks it, exactly as it is for the two beside it.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed partial class KeychainServices : IDesktopSecretStore
{
    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Versions/Current/Security";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/Versions/Current/CoreFoundation";

    /// <summary>The service the item is filed under, which is what separates it from every other application's.</summary>
    private const string ServiceName = "io.github.krzysztof318.mailfathom";

    /// <summary>The account the item is filed under, of which this application has exactly one.</summary>
    /// <remarks>
    /// A fixed name rather than the owner's username. A keychain item's account attribute is searchable metadata rather
    /// than a secret — Keychain Access lists it — so filing the item under the owner's own name would publish who
    /// somebody signs in as while protecting nothing. There is one item, so nothing has to be told apart.
    /// </remarks>
    private const string AccountName = "sign-in";

    /// <summary><c>errSecSuccess</c>.</summary>
    private const int Success = 0;

    /// <summary><c>errSecItemNotFound</c>, which is the ordinary answer for a head that has stored nothing yet.</summary>
    private const int ItemNotFound = -25300;

    /// <summary><c>errSecDuplicateItem</c>, which says the item is there and is to be updated rather than added.</summary>
    private const int DuplicateItem = -25299;

    /// <summary><c>kCFStringEncodingUTF8</c>.</summary>
    private const uint Utf8 = 0x08000100;

    /// <summary><c>kCFAllocatorDefault</c>, which is the null allocator handle.</summary>
    private const nint DefaultAllocator = 0;

    /// <inheritdoc />
    /// <remarks>Every macOS installation this head runs on has a login keychain, so there is nothing to probe for. Whether it will unlock is what a call's own answer reports.</remarks>
    public bool IsReachable => true;

    /// <inheritdoc />
    public string? Read()
    {
        using CoreFoundationScope scope = new();

        var query = scope.Dictionary(
            (Constant("kSecClass"), Constant("kSecClassGenericPassword")),
            (Constant("kSecAttrService"), scope.String(ServiceName)),
            (Constant("kSecAttrAccount"), scope.String(AccountName)),
            (Constant("kSecReturnData"), Constant("kCFBooleanTrue")),
            (Constant("kSecMatchLimit"), Constant("kSecMatchLimitOne")));

        var status = SecItemCopyMatching(query, out var held);

        if (status == ItemNotFound)
        {
            return null;
        }

        if (status != Success)
        {
            throw Refused("read", status);
        }

        scope.Own(held);

        return ReadData(held);
    }

    /// <inheritdoc />
    public void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using CoreFoundationScope scope = new();

        var contents = scope.Data(Encoding.UTF8.GetBytes(value));

        var identity = scope.Dictionary(
            (Constant("kSecClass"), Constant("kSecClassGenericPassword")),
            (Constant("kSecAttrService"), scope.String(ServiceName)),
            (Constant("kSecAttrAccount"), scope.String(AccountName)));

        var added = SecItemAdd(
            scope.Dictionary(
                (Constant("kSecClass"), Constant("kSecClassGenericPassword")),
                (Constant("kSecAttrService"), scope.String(ServiceName)),
                (Constant("kSecAttrAccount"), scope.String(AccountName)),
                (Constant("kSecValueData"), contents)),
            0);

        if (added == Success)
        {
            return;
        }

        if (added != DuplicateItem)
        {
            throw Refused("write", added);
        }

        // The item is already there, which is every sign-in after the first. SecItemAdd refuses a duplicate rather than
        // replacing it, so the second call is what replaces the contents of the one that exists.
        var updated = SecItemUpdate(identity, scope.Dictionary((Constant("kSecValueData"), contents)));

        if (updated != Success)
        {
            throw Refused("replace", updated);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        using CoreFoundationScope scope = new();

        var status = SecItemDelete(scope.Dictionary(
            (Constant("kSecClass"), Constant("kSecClassGenericPassword")),
            (Constant("kSecAttrService"), scope.String(ServiceName)),
            (Constant("kSecAttrAccount"), scope.String(AccountName))));

        if (status is not (Success or ItemNotFound))
        {
            throw Refused("remove", status);
        }
    }

    /// <summary>Reads a <c>CFData</c> the keychain returned back as the text it holds.</summary>
    private static string ReadData(nint data)
    {
        var length = (int)CFDataGetLength(data);

        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = new byte[length];

        Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, length);

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Resolves one of the framework's exported <c>CFStringRef</c> constants by name.</summary>
    /// <remarks>
    /// Keychain Services names its dictionary keys with global constants rather than with literal strings, and the
    /// values behind them are not documented as the names themselves — so they are read from the framework's own export
    /// table. Both frameworks are loaded by absolute path, which is where macOS keeps them and what keeps the lookup
    /// off whatever else is on a search path.
    /// </remarks>
    private static nint Constant(string name)
    {
        var framework = name.StartsWith("kCF", StringComparison.Ordinal) ? CoreFoundation : SecurityFramework;

        try
        {
            return Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(framework), name));
        }
        catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new DesktopSecretStoreUnavailable(
                $"this machine's Security framework does not export {name}, so its keychain cannot be reached",
                missing);
        }
    }

    /// <summary>Turns an <c>OSStatus</c> into the sentence a diagnostic reads before the head falls back to memory.</summary>
    /// <remarks>
    /// The status number and nothing composed from the keychain's own message. <c>SecCopyErrorMessageString</c> would
    /// answer in the operating system's language and is another handle to release for a string nothing puts on a
    /// screen — what reaches a person is one sentence about their next start.
    /// </remarks>
    private static DesktopSecretStoreUnavailable Refused(string attempt, int status) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"the login keychain refused to {attempt} this sign-in (OSStatus {status})"));

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemCopyMatching(nint query, out nint result);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemAdd(nint attributes, nint result);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemUpdate(nint query, nint attributesToUpdate);

    [LibraryImport(SecurityFramework)]
    private static partial int SecItemDelete(nint query);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringCreateWithBytes(
        nint allocator,
        ReadOnlySpan<byte> bytes,
        nint length,
        uint encoding,
        [MarshalAs(UnmanagedType.U1)] bool isExternalRepresentation);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataCreate(nint allocator, ReadOnlySpan<byte> bytes, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryCreate(
        nint allocator,
        nint[] keys,
        nint[] values,
        nint count,
        nint keyCallBacks,
        nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint reference);

    /// <summary>The Core Foundation objects one call created, released together when it returns.</summary>
    /// <remarks>
    /// Core Foundation is reference counted and every <c>Create</c> hands ownership to the caller, so a call that
    /// returned without releasing what it built would leak a password's worth of memory per sign-in. Keeping them in
    /// one place is what makes releasing all of them one statement rather than a chain of <c>finally</c> blocks. The
    /// constants resolved by name are deliberately not owned: they belong to the framework and are not this side's to
    /// release.
    /// </remarks>
    private sealed class CoreFoundationScope : IDisposable
    {
        private readonly List<nint> created = [];

        /// <summary>Takes ownership of a reference something else created, such as a keychain's answer.</summary>
        /// <param name="reference">The reference to release when this scope ends.</param>
        internal void Own(nint reference)
        {
            if (reference != 0)
            {
                this.created.Add(reference);
            }
        }

        /// <summary>Creates a <c>CFString</c>, encoded as UTF-8 as every value here is.</summary>
        internal nint String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var reference = CFStringCreateWithBytes(DefaultAllocator, bytes, bytes.Length, Utf8, false);

            return this.Created(reference, nameof(CFStringCreateWithBytes));
        }

        /// <summary>Creates a <c>CFData</c> holding the octets a value was encoded to.</summary>
        internal nint Data(byte[] value) =>
            this.Created(CFDataCreate(DefaultAllocator, value, value.Length), nameof(CFDataCreate));

        /// <summary>Creates a <c>CFDictionary</c> describing an item, keyed and valued by Core Foundation objects.</summary>
        /// <remarks>
        /// The callbacks are the framework's own, resolved by address: <c>kCFTypeDictionaryKeyCallBacks</c> and
        /// <c>kCFTypeDictionaryValueCallBacks</c> are what make the dictionary retain what it holds, and a dictionary
        /// created without them would hold raw pointers it neither retains nor compares as Core Foundation objects.
        /// </remarks>
        internal nint Dictionary(params (nint Key, nint Value)[] entries)
        {
            var keys = Array.ConvertAll(entries, entry => entry.Key);
            var values = Array.ConvertAll(entries, entry => entry.Value);

            var reference = CFDictionaryCreate(
                DefaultAllocator,
                keys,
                values,
                entries.Length,
                CallBacks("kCFTypeDictionaryKeyCallBacks"),
                CallBacks("kCFTypeDictionaryValueCallBacks"));

            return this.Created(reference, nameof(CFDictionaryCreate));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var reference in this.created)
            {
                CFRelease(reference);
            }

            this.created.Clear();
        }

        /// <summary>Resolves the address of one of Core Foundation's exported callback structures.</summary>
        /// <remarks>The structure itself rather than a pointer to it, which is why the export's own address is passed rather than what is stored there.</remarks>
        private static nint CallBacks(string name)
        {
            try
            {
                return NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), name);
            }
            catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new DesktopSecretStoreUnavailable(
                    $"this machine's Core Foundation does not export {name}, so no keychain query can be composed",
                    missing);
            }
        }

        /// <summary>Records a created reference, or reports the allocation that did not happen.</summary>
        private nint Created(nint reference, string call)
        {
            if (reference == 0)
            {
                throw new DesktopSecretStoreUnavailable($"{call} returned nothing, so the keychain was not reached");
            }

            this.created.Add(reference);

            return reference;
        }
    }
}
