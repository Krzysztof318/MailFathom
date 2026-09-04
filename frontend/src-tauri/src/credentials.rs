// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Where this shell keeps the credential the client signed in with, which is the one operation the web head has no
// equivalent of at all. ADR 0023 decided the question for a desktop keychain and a browser page, and ADR 0027 amended
// it for the Android head: the mechanism there is the Android Keystore rather than the `keyring` crate, which has no
// Android backend, and the fallback is the opposite of the desktop's — a device whose protected storage cannot be
// reached keeps nothing rather than writing the password into the page, because a phone kills the client all day and
// the page is where a script that reached the origin would read it.
//
// That second half is why this module answers with an *arrangement* rather than with a fact about the machine. The
// application asks one question — where will the credential live this run — and only the shell knows which of the two
// answers a store it cannot reach deserves, so the shell states the outcome and no screen underneath ever learns a
// platform's name. The four values below are the vocabulary `signIn/credentialStore.ts` publishes, spelled the same on
// both sides; a value this file adds that the application does not recognise is read there as no store at all, which
// is the arrangement a shell older than the answer was already giving. Each is declared for the heads that can give
// it, which is why three of the four carry a target: a desktop shell has no answer for a store the platform discarded
// a key from, and an Android one never offers the page as a fallback.
//
// The two implementations below are selected by target and are the whole of the difference between the heads. Neither
// reports why anything failed: everything they could report is about a password, and a client told nothing simply asks
// for it again.

/// The shell keeps it in the operating system's own protected store, and only signing out removes it.
pub const KEPT_IN_THE_STORE: &str = "keptInTheStore";

/// The shell has no protected store, and the client may keep it for the run itself.
#[cfg(not(target_os = "android"))]
pub const KEPT_FOR_THE_RUN: &str = "keptForTheRun";

/// The shell has a protected store it could not reach, so nothing is kept anywhere.
#[cfg(target_os = "android")]
pub const NOT_KEPT_STORAGE_UNREACHABLE: &str = "notKeptStorageUnreachable";

/// The operating system discarded the key the store was written under, so nothing is kept and what was is gone.
#[cfg(target_os = "android")]
pub const NOT_KEPT_KEY_INVALIDATED: &str = "notKeptKeyInvalidated";

#[cfg(not(target_os = "android"))]
mod platform {
    use super::{KEPT_FOR_THE_RUN, KEPT_IN_THE_STORE};
    use keyring::Entry;

    /// The service every entry is written under, beside the deployment address the credential was given for.
    const CREDENTIAL_SERVICE: &str = "MailFathom";

    /// Whether this machine offers a credential store at all, which is what the sign-in screen says before anybody types.
    ///
    /// A machine with no Secret Service provider running offers none, and the client then keeps the credential for the
    /// run and says so. Reading the store's initialization rather than writing a probe entry is what keeps this from
    /// leaving anything behind on a machine that is only being asked a question.
    pub async fn arrangement() -> &'static str {
        if Entry::store_status().is_ok() {
            KEPT_IN_THE_STORE
        } else {
            KEPT_FOR_THE_RUN
        }
    }

    /// Keeps the finished header value for one deployment, answering whether it was kept.
    pub async fn keep(deployment: String, authorization: String) -> bool {
        entry(&deployment).is_some_and(|entry| entry.set_password(&authorization).is_ok())
    }

    /// The header value kept for one deployment, or nothing where none was kept or the store would not answer.
    pub async fn read(deployment: String) -> Option<String> {
        entry(&deployment).and_then(|entry| entry.get_password().ok())
    }

    /// Deletes what was kept for one deployment, which is what sign-out does and the only thing that removes it.
    ///
    /// An entry that is already gone is the outcome asked for rather than a failure, so it answers the same as a
    /// deletion.
    pub async fn forget(deployment: String) -> bool {
        entry(&deployment).is_some_and(|entry| {
            matches!(
                entry.delete_credential(),
                Ok(()) | Err(keyring::Error::NoEntry)
            )
        })
    }

    /// The entry a deployment's credential is written under, or nothing where the store could not be reached.
    fn entry(deployment: &str) -> Option<Entry> {
        Entry::new(CREDENTIAL_SERVICE, deployment).ok()
    }
}

#[cfg(target_os = "android")]
mod platform {
    use super::{KEPT_IN_THE_STORE, NOT_KEPT_KEY_INVALIDATED, NOT_KEPT_STORAGE_UNREACHABLE};
    use std::collections::HashMap;
    use std::sync::OnceLock;
    use tauri::plugin::{Builder, PluginHandle, TauriPlugin};
    use tauri::Wry;

    /// What holds the four operations on this head: an ordinary Kotlin class in the head's own application module,
    /// reached through Tauri's Android plugin bridge because the Keystore is Java's and the shell is Rust's.
    const PLUGIN_IDENTIFIER: &str = "io.github.krzysztof318.mailfathom";
    const PLUGIN_CLASS: &str = "CredentialStorePlugin";

    /// The bridge, once the shell has started. Held here rather than in Tauri's state because every caller below is a
    /// command this application defines, and a command that had to be handed an `AppHandle` to reach it would put the
    /// head's shape into a signature the desktop shares.
    static PROTECTED_STORE: OnceLock<PluginHandle<Wry>> = OnceLock::new();

    /// Registers the Kotlin side, which is what makes the four operations below answerable at all.
    pub fn registration() -> TauriPlugin<Wry> {
        Builder::new("credential-store")
            .setup(|_app, api| {
                let handle = api.register_android_plugin(PLUGIN_IDENTIFIER, PLUGIN_CLASS)?;
                let _ = PROTECTED_STORE.set(handle);

                Ok(())
            })
            .build()
    }

    /// What arrangement this device offers, which the Kotlin side answers without creating a key to find out.
    ///
    /// A bridge that is not there, or that would not answer, is protected storage this client cannot reach — which on
    /// this head keeps nothing rather than falling back to the page, per ADR 0027.
    pub async fn arrangement() -> &'static str {
        let Some(store) = PROTECTED_STORE.get() else {
            return NOT_KEPT_STORAGE_UNREACHABLE;
        };

        match store
            .run_mobile_plugin_async::<String>("arrangement", ())
            .await
        {
            Ok(answer) if answer == KEPT_IN_THE_STORE => KEPT_IN_THE_STORE,
            Ok(answer) if answer == NOT_KEPT_KEY_INVALIDATED => NOT_KEPT_KEY_INVALIDATED,
            _ => NOT_KEPT_STORAGE_UNREACHABLE,
        }
    }

    /// Keeps the finished header value for one deployment, answering whether it was kept.
    pub async fn keep(deployment: String, authorization: String) -> bool {
        let Some(store) = PROTECTED_STORE.get() else {
            return false;
        };

        store
            .run_mobile_plugin_async::<bool>(
                "keep",
                HashMap::from([("deployment", deployment), ("authorization", authorization)]),
            )
            .await
            .unwrap_or(false)
    }

    /// The header value kept for one deployment, or nothing where none was kept or the store would not answer.
    pub async fn read(deployment: String) -> Option<String> {
        let store = PROTECTED_STORE.get()?;

        store
            .run_mobile_plugin_async::<Option<String>>(
                "read",
                HashMap::from([("deployment", deployment)]),
            )
            .await
            .ok()
            .flatten()
    }

    /// Deletes what was kept for one deployment, which is what sign-out does and the only thing that removes it.
    pub async fn forget(deployment: String) -> bool {
        let Some(store) = PROTECTED_STORE.get() else {
            return false;
        };

        store
            .run_mobile_plugin_async::<bool>(
                "forget",
                HashMap::from([("deployment", deployment)]),
            )
            .await
            .unwrap_or(false)
    }
}

pub use platform::{arrangement, forget, keep, read};

#[cfg(target_os = "android")]
pub use platform::registration;
