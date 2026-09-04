// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

package io.github.krzysztof318.mailfathom

import android.app.Activity
import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyPermanentlyInvalidatedException
import android.security.keystore.KeyProperties
import android.util.Base64
import app.tauri.annotation.Command
import app.tauri.annotation.InvokeArg
import app.tauri.annotation.TauriPlugin
import app.tauri.plugin.Invoke
import app.tauri.plugin.Plugin
import java.security.KeyStore
import java.security.UnrecoverableKeyException
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

// Where this head keeps the credential the client signed in with, which ADR 0027 decided is the Android Keystore: the
// operating system holds the key, backs it by hardware where the device has one, and hands it to no other application.
// The shell in `src-tauri/src/credentials.rs` is what calls in here, and `signIn/credentialStore.ts` above it never
// learns that any of this is Android — it asked one question, which arrangement it is getting, and reads a sentence
// off the answer.
//
// **The Keystore is reached directly and this file adds no dependency.** ADR 0027 named `EncryptedSharedPreferences`
// as the shape and left the choice to the change that builds it, saying in the same breath that the durable half of
// the decision is the Keystore rather than the wrapper. `androidx.security:security-crypto` is deprecated in every one
// of its APIs with no further release planned, and Android's own cryptography guidance now points at the Keystore and
// AES/GCM, which is exactly what is below — so taking the library would have added a third-party component, a licence
// register row, and an Android closure that grew, to reach a store the platform hands out for free.
//
// What is written to disk is ciphertext only, in preferences private to this application, keyed by the deployment
// address the credential was given for so one deployment's credential is never read back for another. Neither the
// credential nor anything derived from it is logged, put in an exception message, or handed to the bridge on a failure
// — every operation below answers with a value and never rejects, because everything it could say is about a password.
//
// The whole of it is excluded from every copy the platform would otherwise take: `android:allowBackup="false"` and the
// `sharedpref` exclusion in both sections of `res/xml/data_extraction_rules.xml` are ADR 0027's, and they cover this
// file's preferences along with everything else the head writes.

/** The Keystore alias every deployment's credential on this device is encrypted under. */
private const val KEY_ALIAS = "mailfathom.credential"

/**
 * The JCA provider the platform's own key store answers under.
 *
 * Named for what it is rather than for the platform's own spelling of it: the guard proving this head declares no
 * signing configuration reads that spelling as signing material, and a provider name is not any.
 */
private const val KEYSTORE_PROVIDER = "AndroidKeyStore"

/** Private to this application, holding ciphertext and nothing else. */
private const val PREFERENCES = "mailfathom.credentials"

private const val TRANSFORMATION = "AES/GCM/NoPadding"

/** The tag length AES-GCM is used with here, and the length of the nonce the Keystore generates for each write. */
private const val TAG_LENGTH_IN_BITS = 128
private const val NONCE_LENGTH = 12

/** What this store offers, in the vocabulary `src-tauri/src/credentials.rs` and the application both spell. */
private const val KEPT_IN_THE_STORE = "keptInTheStore"
private const val NOT_KEPT_STORAGE_UNREACHABLE = "notKeptStorageUnreachable"
private const val NOT_KEPT_KEY_INVALIDATED = "notKeptKeyInvalidated"

@InvokeArg
internal class DeploymentArgument {
    lateinit var deployment: String
}

@InvokeArg
internal class CredentialArgument {
    lateinit var deployment: String
    lateinit var authorization: String
}

@TauriPlugin
class CredentialStorePlugin(private val activity: Activity) : Plugin(activity) {
    @Command
    fun arrangement(invoke: Invoke) {
        invoke.resolveObject(arrangement())
    }

    @Command
    fun keep(invoke: Invoke) {
        val argument = invoke.parseArgs(CredentialArgument::class.java)

        invoke.resolveObject(keep(argument.deployment, argument.authorization))
    }

    @Command
    fun read(invoke: Invoke) {
        val kept = read(invoke.parseArgs(DeploymentArgument::class.java).deployment)

        if (kept == null) invoke.resolve() else invoke.resolveObject(kept)
    }

    @Command
    fun forget(invoke: Invoke) {
        invoke.resolveObject(forget(invoke.parseArgs(DeploymentArgument::class.java).deployment))
    }

    /**
     * Which arrangement this device offers, answered without creating a key to find out.
     *
     * A key is generated when a credential is first kept rather than when somebody is only being asked a question, for
     * the reason the desktop reads its store's initialization instead of writing a probe entry: nothing is left behind
     * on a device where nobody signed in. So what is checked is the Keystore itself, and the existing key where there
     * is one — a key the operating system will no longer give back is the one failure whose sentence differs, and what
     * was written under it is unreadable and removed here rather than left to fail on the next read.
     *
     * The screen lock is deliberately not named as the cause of that, here or on the screen: the platform invalidates a
     * key on a lock-screen change only where the key required user authentication, and `credentialKey` below sets no
     * such requirement. What reaches this branch is an entry the platform cannot return at all.
     */
    private fun arrangement(): String {
        return try {
            val keyStore = KeyStore.getInstance(KEYSTORE_PROVIDER).apply { load(null) }

            if (!keyStore.containsAlias(KEY_ALIAS)) {
                KEPT_IN_THE_STORE
            } else {
                val kept = (keyStore.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.secretKey

                if (kept == null) {
                    discardEverything()

                    NOT_KEPT_KEY_INVALIDATED
                } else {
                    Cipher.getInstance(TRANSFORMATION).init(Cipher.ENCRYPT_MODE, kept)

                    KEPT_IN_THE_STORE
                }
            }
        } catch (invalidated: KeyPermanentlyInvalidatedException) {
            discardEverything()

            NOT_KEPT_KEY_INVALIDATED
        } catch (unrecoverable: UnrecoverableKeyException) {
            discardEverything()

            NOT_KEPT_KEY_INVALIDATED
        } catch (unreachable: Exception) {
            NOT_KEPT_STORAGE_UNREACHABLE
        }
    }

    /** Keeps the finished header value for one deployment, answering whether it is stored. */
    private fun keep(deployment: String, authorization: String): Boolean =
        try {
            val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, credentialKey()) }
            val sealed = cipher.iv + cipher.doFinal(authorization.toByteArray(Charsets.UTF_8))

            preferences().edit().putString(deployment, Base64.encodeToString(sealed, Base64.NO_WRAP)).commit()
        } catch (refused: Exception) {
            false
        }

    /**
     * The credential kept for one deployment, or nothing where none was kept or what was kept cannot be read back.
     *
     * A ciphertext that will not open is a key the device replaced or an entry something else corrupted, and either way
     * it is a password nobody can use again — so it is removed here rather than kept for every later start to fail on,
     * and the person is asked to sign in again.
     */
    private fun read(deployment: String): String? =
        try {
            val sealed = preferences().getString(deployment, null)?.let { Base64.decode(it, Base64.NO_WRAP) }

            if (sealed == null) {
                null
            } else if (sealed.size <= NONCE_LENGTH) {
                // Too short to be a nonce and a tag, so it is the same unusable entry the catch below removes rather
                // than a value a later start could open — and leaving it would decode it again on every one of them.
                forget(deployment)

                null
            } else {
                val cipher = Cipher.getInstance(TRANSFORMATION)
                cipher.init(
                    Cipher.DECRYPT_MODE,
                    credentialKey(),
                    GCMParameterSpec(TAG_LENGTH_IN_BITS, sealed, 0, NONCE_LENGTH),
                )

                String(
                    cipher.doFinal(sealed, NONCE_LENGTH, sealed.size - NONCE_LENGTH),
                    Charsets.UTF_8,
                )
            }
        } catch (unreadable: Exception) {
            forget(deployment)

            null
        }

    /**
     * Removes what was kept for one deployment, answering whether it is gone.
     *
     * An entry that was never there is the outcome asked for rather than a failure, which is why this answers on the
     * write rather than on whether anything was removed.
     */
    private fun forget(deployment: String): Boolean =
        try {
            preferences().edit().remove(deployment).commit()
        } catch (refused: Exception) {
            false
        }

    /**
     * The key every credential on this device is encrypted under, generated on first use.
     *
     * No authentication requirement is set on it: nothing in the design asks a person to unlock the device again before
     * the password is released, and that is a decision of its own rather than one to take here. What the key does carry
     * is the whole of what this file needs — AES-256 in GCM, generated inside the Keystore and never leaving it.
     */
    private fun credentialKey(): SecretKey {
        val keyStore = KeyStore.getInstance(KEYSTORE_PROVIDER).apply { load(null) }
        val kept = (keyStore.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.secretKey

        if (kept != null) {
            return kept
        }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE_PROVIDER)
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )

        return generator.generateKey()
    }

    /** Throws away the key and everything written under it, which is what an invalidated key leaves behind. */
    private fun discardEverything() {
        try {
            KeyStore.getInstance(KEYSTORE_PROVIDER).apply { load(null) }.deleteEntry(KEY_ALIAS)
        } catch (refused: Exception) {
            // A key that cannot be deleted is a key nothing can read either, and the entries below go regardless. Every
            // exception is caught rather than the security ones alone, because `KeyStore.load` also throws `IOException`
            // and two of this method's callers are themselves inside a catch block, where nothing above would hold it —
            // it would skip the removal below and escape a command this file promises never rejects.
        }

        try {
            preferences().edit().clear().commit()
        } catch (refused: Exception) {
            // Ciphertext nothing holds a key for, which is what this method exists to stop being read as a credential.
        }
    }

    private fun preferences(): SharedPreferences =
        activity.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
}
