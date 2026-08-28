// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MailFathom.Client.Platforms.Desktop.Credentials;

/// <summary>Holds the sign-in in the session's Secret Service collection, through <c>libsecret</c>.</summary>
/// <remarks>
/// <para>
/// The Secret Service API is what GNOME Keyring and KWallet both implement, so one implementation reaches the store a
/// Linux desktop actually has. The item goes into the session's default collection, protected by whatever unlocks that
/// collection, and is therefore not readable from a copy of the home directory the way a file beside the application
/// would be.
/// </para>
/// <para>
/// A great many sessions have none of it — a container, a bare X session, a remote display with no keyring agent. That
/// is the ordinary case rather than a broken one: <c>libsecret</c> may not be installed, there may be no D-Bus session
/// bus to reach a provider over, no provider may be running, and a collection that exists may be locked. All four
/// arrive as <see cref="DesktopSecretStoreUnavailable" />, the credential stays in memory for the run, and the sign-in
/// screen says the next start will ask again.
/// </para>
/// <para>
/// A fifth case answers to none of those and is why every call carries a <c>GCancellable</c>: a provider that owns the
/// session-bus name and does not answer. A wedged daemon, or a locked collection whose unlock prompt is raised on a
/// display nobody is watching, would otherwise leave the calling thread inside <c>libsecret</c> for the life of the
/// process — and a window that stops responding while somebody signs in is worse than one that falls back, because
/// nothing about it says what happened.
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
internal sealed partial class SecretServiceKeyring : IDesktopSecretStore
{
    private const string Secret = "libsecret-1.so.0";

    private const string Glib = "libglib-2.0.so.0";

    /// <summary>Where <c>GCancellable</c> lives, which is GIO rather than glib proper.</summary>
    private const string Gio = "libgio-2.0.so.0";

    /// <summary>Where the reference counting every GIO object is released through lives.</summary>
    private const string GObject = "libgobject-2.0.so.0";

    /// <summary><c>SECRET_COLLECTION_DEFAULT</c>: the session's own collection rather than a named one.</summary>
    private const string DefaultCollection = "default";

    /// <summary>How the item is labelled, which is the only place a person ever reads it.</summary>
    private const string ItemLabel = "MailFathom sign-in";

    /// <summary>How long a provider is given to answer before the call is withdrawn.</summary>
    /// <remarks>
    /// Long because the legitimate slow case is a person: a locked collection raises an unlock prompt, and somebody
    /// reading it, finding their password, and typing it takes tens of seconds. Bounded because the illegitimate slow
    /// case never ends, and a window whose sign-in never returns is a window that appears to have crashed.
    /// </remarks>
    private static readonly TimeSpan ProviderDeadline = TimeSpan.FromSeconds(30);

    private static readonly Lazy<nint> StringHash = new(() => GlibFunction("g_str_hash"));

    private static readonly Lazy<nint> StringEqual = new(() => GlibFunction("g_str_equal"));

    /// <summary>Makes one call into <c>libsecret</c>, whose failure arrives beside its answer rather than in place of it.</summary>
    /// <param name="cancellable">The <c>GCancellable</c> the deadline cancels, which the call is to be given.</param>
    /// <param name="error">The <c>GError</c> the call allocated, or zero when it succeeded.</param>
    /// <returns>Whatever the call returned, which is a pointer for a lookup and a truth value for the other two.</returns>
    private delegate nint Exchange(nint cancellable, out nint error);

    /// <inheritdoc />
    /// <remarks>
    /// Answered from the two places a session bus announces itself rather than by writing anything. It does not prove a
    /// provider is running or that its collection will unlock — those are what a call's own answer reports — but it is
    /// the difference between a desktop session and a host that never had one, which is the case worth saying plainly
    /// before somebody types a password.
    /// </remarks>
    public bool IsReachable => HasSessionBus();

    /// <inheritdoc />
    public string? Read()
    {
        RequireSessionBus();

        var attributes = Describe();

        try
        {
            var held = Call((nint cancellable, out nint error) =>
                PasswordLookup(0, attributes.Table, cancellable, out error));

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
    public void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        RequireSessionBus();

        var attributes = Describe();

        try
        {
            var stored = Call((nint cancellable, out nint error) => PasswordStore(
                0,
                attributes.Table,
                DefaultCollection,
                ItemLabel,
                value,
                cancellable,
                out error));

            if (stored == 0)
            {
                throw new DesktopSecretStoreUnavailable(
                    "the Secret Service did not store this sign-in and reported no reason for it");
            }
        }
        finally
        {
            attributes.Release();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        RequireSessionBus();

        var attributes = Describe();

        try
        {
            _ = Call((nint cancellable, out nint error) =>
                PasswordClear(0, attributes.Table, cancellable, out error));
        }
        finally
        {
            attributes.Release();
        }
    }

    /// <summary>Reports whether this session has a bus a Secret Service provider could be reached over.</summary>
    /// <remarks>
    /// The two places one announces itself are the variable every desktop session exports and the socket
    /// <c>systemd</c> places in the user's runtime directory. Neither being there is a session with no desktop behind
    /// it.
    /// </remarks>
    private static bool HasSessionBus()
    {
        if (Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 })
        {
            return true;
        }

        return Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtimeDirectory
            && File.Exists(Path.Combine(runtimeDirectory, "bus"));
    }

    /// <summary>Refuses early where there is demonstrably no session bus to reach a provider over.</summary>
    /// <remarks><c>libsecret</c> would fail on its own, but later and with a message about a D-Bus connection rather than about this session.</remarks>
    private static void RequireSessionBus()
    {
        if (!HasSessionBus())
        {
            throw new DesktopSecretStoreUnavailable(
                "this session has no D-Bus session bus, so no Secret Service provider can be reached");
        }
    }

    /// <summary>Runs one <c>libsecret</c> call under a deadline, turning everything that can go wrong into one exception.</summary>
    /// <remarks>
    /// The library being absent, the library refusing, and a provider that never answers are the same case to a caller,
    /// so all three leave here as <see cref="DesktopSecretStoreUnavailable" />. A <c>GError</c> belongs to this side
    /// once it is set, which is why it is read and freed here rather than at each call site.
    /// <para>
    /// The withdrawal is read back from the <c>GCancellable</c> rather than remembered on this side, because the thread
    /// that would set a flag is the one blocked in the call. It decides only what the failure is called: a cancelled
    /// call reports <c>G_IO_ERROR_CANCELLED</c>, whose message says an operation was cancelled and would read as
    /// something the person did.
    /// </para>
    /// </remarks>
    private static nint Call(Exchange exchange)
    {
        nint result;
        nint failure;
        nint cancellable = 0;
        var withdrawn = false;

        try
        {
            cancellable = CancellableNew();

            // A timer rather than anything the calling thread could check: that thread is inside libsecret until the
            // call returns, so whatever ends the wait has to run elsewhere, and cancelling is safe from any thread.
            using var deadline = new Timer(Withdraw, cancellable, ProviderDeadline, Timeout.InfiniteTimeSpan);

            try
            {
                result = exchange(cancellable, out failure);
                withdrawn = CancellableIsCancelled(cancellable) != 0;
            }
            finally
            {
                // Retired here rather than left to the declaration above, because only this form waits for a
                // cancellation already running — and that callback holds the handle released below. What the
                // declaration then disposes is a timer already gone, which is a no-op.
                StopWaitingOn(deadline);
            }
        }
        catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new DesktopSecretStoreUnavailable(
                "libsecret and the glib libraries it is reached through are not installed here, so there is no Secret Service to reach",
                missing);
        }
        finally
        {
            if (cancellable != 0)
            {
                ObjectUnref(cancellable);
            }
        }

        if (failure == 0)
        {
            return result;
        }

        try
        {
            throw withdrawn
                ? new DesktopSecretStoreUnavailable(
                    $"the Secret Service did not answer within {ProviderDeadline.TotalSeconds:F0} seconds, so the call was withdrawn")
                : new DesktopSecretStoreUnavailable("the Secret Service refused this sign-in");
        }
        finally
        {
            ErrorFree(failure);
        }
    }

    /// <summary>Ends the wait on a provider that has had its time.</summary>
    private static void Withdraw(object? cancellable) => CancellableCancel((nint)cancellable!);

    /// <summary>Retires the deadline and waits for a cancellation already under way to finish.</summary>
    /// <remarks>
    /// Waiting is the whole point of this overload: an ordinary <c>Dispose</c> returns while a callback may still be
    /// running, and that callback holds the <c>GCancellable</c> the caller is about to release.
    /// </remarks>
    private static void StopWaitingOn(Timer deadline)
    {
        using ManualResetEvent stopped = new(false);

        if (deadline.Dispose(stopped))
        {
            stopped.WaitOne();
        }
    }

    /// <summary>Builds the attribute set the one item is addressed by.</summary>
    /// <remarks>
    /// Two attributes rather than a composed name: the application so that nothing else's item is ever read, and what
    /// the item is so that a later secret of this application's would not collide with it. The deployment address is
    /// deliberately not among them — a keyring's attributes are searchable metadata rather than a secret, and there is
    /// one item, so putting it there would publish which server somebody uses while protecting nothing.
    /// </remarks>
    private static Attributes Describe() => new(
        ("application", "MailFathom"),
        ("secret", "sign-in"));

    /// <summary>Resolves one <c>glib</c> function by address, which is how <c>g_hash_table_new</c> takes its two.</summary>
    private static nint GlibFunction(string name)
    {
        try
        {
            return NativeLibrary.GetExport(NativeLibrary.Load(Glib), name);
        }
        catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new DesktopSecretStoreUnavailable(
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

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Gio, EntryPoint = "g_cancellable_new")]
    private static partial nint CancellableNew();

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Gio, EntryPoint = "g_cancellable_cancel")]
    private static partial void CancellableCancel(nint cancellable);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(Gio, EntryPoint = "g_cancellable_is_cancelled")]
    private static partial int CancellableIsCancelled(nint cancellable);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(nint instance);

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
