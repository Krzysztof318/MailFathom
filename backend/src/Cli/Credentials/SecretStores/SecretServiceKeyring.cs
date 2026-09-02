// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// A fifth case answers to none of those and is why every call carries a <c>GCancellable</c>: a provider that owns the
/// session-bus name and does not answer. A wedged daemon, or a locked collection whose unlock prompt is raised on a
/// display nobody is watching, would otherwise leave the calling thread inside <c>libsecret</c> for the life of the
/// process — and a command that hangs is worse than one that falls back, because nothing about it says what happened.
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

    /// <summary>Where <c>GCancellable</c> lives, which is GIO rather than glib proper.</summary>
    private const string Gio = "libgio-2.0.so.0";

    /// <summary>Where the reference counting every GIO object is released through lives.</summary>
    private const string GObject = "libgobject-2.0.so.0";

    /// <summary><c>SECRET_COLLECTION_DEFAULT</c>: the session's own collection rather than a named one.</summary>
    private const string DefaultCollection = "default";

    /// <summary>Where the message sits in a <c>GError</c>, which is a <c>GQuark</c> and a <c>gint</c> ahead of it.</summary>
    /// <remarks>
    /// Read as an offset rather than through a declared structure, because the marshaller's own structure readers carry
    /// a dynamic-code requirement the trimmed publish reports — and two fixed-width integers before a pointer is the
    /// same eight bytes on every architecture this command is published for.
    /// </remarks>
    private const int FailureMessageOffset = 8;

    /// <summary>How long a provider is given to answer before the call is withdrawn.</summary>
    /// <remarks>
    /// Long because the legitimate slow case is a person: a locked collection raises an unlock prompt, and somebody
    /// reading it, finding their password, and typing it takes tens of seconds. Bounded because the illegitimate slow
    /// case never ends, and this command exits after every invocation, so a wedged provider would otherwise make every
    /// command against every profile hang rather than fall back to the file it was already able to read.
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
    public string Description => "the Secret Service through libsecret";

    /// <inheritdoc />
    public string? Read(ProfileSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        RequireSessionBus();

        var attributes = Describe(secret);

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
    public void Write(ProfileSecret secret, string value)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(value);

        RequireSessionBus();

        var attributes = Describe(secret);

        try
        {
            var stored = Call((nint cancellable, out nint error) => PasswordStore(
                0,
                attributes.Table,
                DefaultCollection,
                LabelOf(secret),
                value,
                cancellable,
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
            return Call((nint cancellable, out nint error) =>
                PasswordClear(0, attributes.Table, cancellable, out error)) != 0;
        }
        finally
        {
            attributes.Release();
        }
    }

    /// <summary>Names the item the way a keyring application lists it, which is the only place a person ever reads it.</summary>
    private static string LabelOf(ProfileSecret secret) =>
        $"MailFathom mfctl {secret.Kind} for {secret.Address} ({secret.Profile})";

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

    /// <summary>Runs one <c>libsecret</c> call under a deadline, turning everything that can go wrong into one exception.</summary>
    /// <remarks>
    /// The library being absent, the library refusing, and a provider that never answers are the same case to a
    /// caller, so all three leave here as <see cref="SecretStoreUnavailable" />. A <c>GError</c> belongs to this side
    /// once it is set, which is why it is read and freed here rather than at each call site.
    /// <para>
    /// The withdrawal is read back from the <c>GCancellable</c> rather than remembered on this side, because the
    /// thread that would set a flag is the one blocked in the call. It decides only what the failure is called: a
    /// cancelled call reports <c>G_IO_ERROR_CANCELLED</c>, whose message says an operation was cancelled and would
    /// leave an operator reading that as something they did.
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
            throw new SecretStoreUnavailable(
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
            if (withdrawn)
            {
                throw new SecretStoreUnavailable(
                    $"the Secret Service did not answer within {ProviderDeadline.TotalSeconds:F0} seconds, so the call was withdrawn");
            }

            var reported = ProviderReportedText.Sanitize(
                Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(failure, FailureMessageOffset)));

            throw new SecretStoreUnavailable(
                $"the Secret Service refused this credential ({reported ?? "no reason reported"})");
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

    /// <summary>Builds the attribute set one entry is addressed by.</summary>
    /// <remarks>
    /// Four attributes rather than one composed string, because this is what the store matches on: the application so
    /// that nothing else's item is ever read, the deployment so that one deployment's credential is never presented to
    /// another, the profile so that two profiles at one deployment keep their own credentials, and which of the
    /// profile's two secrets it is.
    /// </remarks>
    private static Attributes Describe(ProfileSecret secret) => new(
        ("application", "mfctl"),
        ("deployment", secret.Address),
        ("profile", secret.Profile),
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
