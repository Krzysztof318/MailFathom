// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MailFathom.Cli.Credentials.SecretStores;

/// <summary>Holds a profile's secrets in the session's Secret Service collection, through <c>libsecret</c>.</summary>
/// <remarks>
/// <para>
/// The Secret Service API is what GNOME Keyring and KWallet both implement, so one implementation reaches the store a
/// Linux desktop actually has. The item goes into the session's default collection, protected by whatever unlocks that
/// collection, and is therefore not readable from a copy of the home directory the way a key file beside the store is.
/// </para>
/// <para>
/// A great many machines this command runs on have none of it — a headless server, a jump host, a container. That is
/// the ordinary case rather than a broken one: <c>libsecret</c> may not be installed, there may be no D-Bus session
/// bus to reach a provider over, no provider may be running, and a collection that exists may be locked. All four
/// arrive here as <see cref="SecretStoreUnavailable" />, and the command goes on with the sealed credentials file.
/// </para>
/// <para>
/// The <c>v</c> forms of the password functions are the ones taken deliberately. Their variadic siblings would have to
/// be called through a declaration that is not variadic, which this platform's calling convention does not define;
/// these take the attributes as a <c>GHashTable</c> instead, so every call here has the arity its declaration states.
/// That is what the <c>glib</c> imports are for, and the reason the hash and equality functions are resolved by
/// address: <c>g_hash_table_new</c> takes them as arguments, and a table keyed by strings has to hash them as strings.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class SecretServiceKeyring : IOperatorSecretStore
{
    private const string Secret = "libsecret-1.so.0";

    private const string Glib = "libglib-2.0.so.0";

    /// <summary><c>SECRET_COLLECTION_DEFAULT</c>: the session's own collection rather than a named one.</summary>
    private const string DefaultCollection = "default";

    /// <summary>Where the message sits in a <c>GError</c>, which is a <c>GQuark</c> and a <c>gint</c> ahead of it.</summary>
    /// <remarks>
    /// Read as an offset rather than through a declared structure, because the marshaller's own structure readers carry
    /// a dynamic-code requirement the trimmed publish reports — and two fixed-width integers before a pointer is the
    /// same eight bytes on every architecture this command is published for.
    /// </remarks>
    private const int FailureMessageOffset = 8;

    private static readonly Lazy<nint> StringHash = new(() => GlibFunction("g_str_hash"));

    private static readonly Lazy<nint> StringEqual = new(() => GlibFunction("g_str_equal"));

    /// <summary>Makes one call into <c>libsecret</c>, whose failure arrives beside its answer rather than in place of it.</summary>
    /// <param name="error">The <c>GError</c> the call allocated, or zero when it succeeded.</param>
    /// <returns>Whatever the call returned, which is a pointer for a lookup and a truth value for the other two.</returns>
    private delegate nint Exchange(out nint error);

    /// <inheritdoc />
    public string Description => "the Secret Service through libsecret";

    /// <inheritdoc />
    public string? Read(ProfileSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        RequireSessionBus();

        var attributes = Describe(secret);

        try
        {
            var held = Call((out nint error) => PasswordLookup(0, attributes.Table, 0, out error));

            if (held == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8(held);
            }
            finally
            {
                PasswordFree(held);
            }
        }
        finally
        {
            attributes.Release();
        }
    }

    /// <inheritdoc />
    public void Write(ProfileSecret secret, string value)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(value);

        RequireSessionBus();

        var attributes = Describe(secret);

        try
        {
            var stored = Call((out nint error) => PasswordStore(
                0,
                attributes.Table,
                DefaultCollection,
                LabelOf(secret),
                value,
                0,
                out error));

            if (stored == 0)
            {
                throw new SecretStoreUnavailable(
                    "the Secret Service did not store this credential and reported no reason for it");
            }
        }
        finally
        {
            attributes.Release();
        }
    }

    /// <inheritdoc />
    public bool Clear(ProfileSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        RequireSessionBus();

        var attributes = Describe(secret);

        try
        {
            return Call((out nint error) => PasswordClear(0, attributes.Table, 0, out error)) != 0;
        }
        finally
        {
            attributes.Release();
        }
    }

    /// <summary>Names the item the way a keyring application lists it, which is the only place a person ever reads it.</summary>
    private static string LabelOf(ProfileSecret secret) =>
        $"MailFathom mfctl {secret.Kind} for {secret.Address}";

    /// <summary>Refuses early where there is demonstrably no session bus to reach a provider over.</summary>
    /// <remarks>
    /// <c>libsecret</c> would fail on its own, but later and with a message about a D-Bus connection rather than about
    /// this machine. The two places a session bus announces itself are the variable every desktop session exports and
    /// the socket <c>systemd</c> places in the user's runtime directory; neither being there is a headless host, which
    /// is the case worth naming plainly.
    /// </remarks>
    private static void RequireSessionBus()
    {
        if (Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 })
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtimeDirectory
            && File.Exists(Path.Combine(runtimeDirectory, "bus")))
        {
            return;
        }

        throw new SecretStoreUnavailable(
            "this session has no D-Bus session bus, so no Secret Service provider can be reached");
    }

    /// <summary>Runs one <c>libsecret</c> call, turning everything that can go wrong into one exception.</summary>
    /// <remarks>
    /// The library being absent and the library refusing are the same case to a caller, so both leave here as
    /// <see cref="SecretStoreUnavailable" />. A <c>GError</c> belongs to this side once it is set, which is why it is
    /// read and freed here rather than at each call site.
    /// </remarks>
    private static nint Call(Exchange exchange)
    {
        nint result;
        nint failure;

        try
        {
            result = exchange(out failure);
        }
        catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreUnavailable(
                "libsecret is not installed here, so there is no Secret Service to reach", missing);
        }

        if (failure == 0)
        {
            return result;
        }

        try
        {
            var reported = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(failure, FailureMessageOffset));

            throw new SecretStoreUnavailable(
                $"the Secret Service refused this credential ({reported ?? "no reason reported"})");
        }
        finally
        {
            ErrorFree(failure);
        }
    }

    /// <summary>Builds the attribute set one entry is addressed by.</summary>
    /// <remarks>
    /// Three attributes rather than one composed string, because this is what the store matches on: the application so
    /// that nothing else's item is ever read, the deployment so that one deployment's credential is never presented to
    /// another, and which of the profile's two secrets it is.
    /// </remarks>
    private static Attributes Describe(ProfileSecret secret) => new(
        ("application", "mfctl"),
        ("deployment", secret.Address),
        ("secret", secret.Kind));

    /// <summary>Resolves one <c>glib</c> function by address, which is how <c>g_hash_table_new</c> takes its two.</summary>
    private static nint GlibFunction(string name)
    {
        try
        {
            return NativeLibrary.GetExport(NativeLibrary.Load(Glib), name);
        }
        catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreUnavailable(
                "glib is not installed here, so there is no Secret Service to reach", missing);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Secret, EntryPoint = "secret_password_lookupv_sync")]
    private static partial nint PasswordLookup(nint schema, nint attributes, nint cancellable, out nint error);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Secret, EntryPoint = "secret_password_storev_sync", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PasswordStore(
        nint schema,
        nint attributes,
        string collection,
        string label,
        string password,
        nint cancellable,
        out nint error);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Secret, EntryPoint = "secret_password_clearv_sync")]
    private static partial int PasswordClear(nint schema, nint attributes, nint cancellable, out nint error);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Secret, EntryPoint = "secret_password_free")]
    private static partial void PasswordFree(nint password);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Glib, EntryPoint = "g_hash_table_new")]
    private static partial nint HashTableNew(nint hash, nint equal);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Glib, EntryPoint = "g_hash_table_insert")]
    private static partial void HashTableInsert(nint table, nint key, nint value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Glib, EntryPoint = "g_hash_table_unref")]
    private static partial void HashTableUnref(nint table);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Glib, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(nint error);

    /// <summary>A <c>GHashTable</c> of attributes, and the unmanaged strings it points at.</summary>
    /// <remarks>
    /// The table is created without free functions, so its keys and values are this side's to release; keeping them
    /// beside the handle is what makes releasing all of it one call. Deliberately not <see cref="IDisposable" />:
    /// everything here lives inside one synchronous call, and the pattern would only add a second way to free it.
    /// </remarks>
    private readonly struct Attributes
    {
        private readonly nint[] allocations;

        internal Attributes(params (string Name, string Value)[] pairs)
        {
            this.Table = HashTableNew(StringHash.Value, StringEqual.Value);
            this.allocations = new nint[pairs.Length * 2];

            for (var index = 0; index < pairs.Length; index++)
            {
                var name = Marshal.StringToCoTaskMemUTF8(pairs[index].Name);
                var value = Marshal.StringToCoTaskMemUTF8(pairs[index].Value);

                this.allocations[index * 2] = name;
                this.allocations[(index * 2) + 1] = value;

                HashTableInsert(this.Table, name, value);
            }
        }

        /// <summary>Gets the table the call is given.</summary>
        internal nint Table { get; }

        /// <summary>Frees the table and every string it points at.</summary>
        internal void Release()
        {
            HashTableUnref(this.Table);

            foreach (var allocation in this.allocations)
            {
                Marshal.FreeCoTaskMem(allocation);
            }
        }
    }
}
