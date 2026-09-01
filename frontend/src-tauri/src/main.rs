// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The desktop head, and the whole of it. The shell owns the window, the application identity, and the one thing the web
// head has no equivalent of: the operating system's own credential store. Every other behaviour the client has belongs
// to the bundle it wraps, which is what keeps a screen one screen across both heads.
//
// The four commands below are what ADR 0023 decided this shell would offer, and they are the application's only reach
// into Rust that this repository wrote. They are commands this application defines rather than a plugin's, so the
// capability system does not gate them and no `capabilities/` file names them; what does reach them is the Tauri API
// the WebView is given through `withGlobalTauri` in `tauri.conf.json`, which is why this shell pins no JavaScript
// binding of its own.
//
// Each is `async` for one reason: Tauri runs a synchronous command on the main thread, and every one of these ends in
// a blocking call to a credential store — a D-Bus round trip on Linux, which waits while a locked keyring asks its owner
// to unlock it. Run there, that call freezes the window rather than the request; run on the async runtime, it occupies a
// worker and the application keeps painting.
//
// None of them reports why it failed. Everything they could report is about a password, and a client that is told
// nothing simply asks for it again — which is the same outcome a browser refusing storage produces on the other head.
//
// One plugin is registered beside them, and it answers the other operation the bundle asks of a shell: handing a link a
// reader followed to the system browser, per ADR 0024. That one is a plugin's command, so it is gated —
// `capabilities/open-a-link.json` narrows what the webview may ask of it to the three schemes a message body may carry
// rather than taking the plugin's own default set, and no core capability is granted either.
//
// There is no library target beside this one. The template's split exists so that `tauri android init` and
// `tauri ios init` have a `mobile_entry_point` to attach to, and ADR 0021 supports no mobile head: the tree stays
// reachable for one because the application source carries no platform branch, not because this crate is shaped for a
// target nothing builds.

// A Windows release opens a console window beside the application without this, because a Rust binary is a console
// subsystem executable by default. A debug build keeps it, which is where the WebView's own diagnostics are read.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use keyring::Entry;

/// The service every entry is written under, beside the deployment address the credential was given for.
const CREDENTIAL_SERVICE: &str = "MailFathom";

/// Whether this machine offers a credential store at all, which is what the sign-in screen tells a person before they type.
///
/// A machine with no Secret Service provider running answers `false`, and the client then keeps the credential for the
/// run and says so. Reading the store's initialization rather than writing a probe entry is what keeps this from
/// leaving anything behind on a machine that is only being asked a question.
#[tauri::command]
async fn keychain_reachable() -> bool {
    Entry::store_status().is_ok()
}

/// Keeps the finished header value for one deployment, answering whether it was kept.
#[tauri::command]
async fn keep_credential(deployment: String, authorization: String) -> bool {
    entry(&deployment).is_some_and(|entry| entry.set_password(&authorization).is_ok())
}

/// The header value kept for one deployment, or nothing where none was kept or the store would not answer.
#[tauri::command]
async fn read_credential(deployment: String) -> Option<String> {
    entry(&deployment).and_then(|entry| entry.get_password().ok())
}

/// Deletes what was kept for one deployment, which is what sign-out does and the only thing that removes it.
///
/// An entry that is already gone is the outcome asked for rather than a failure, so it answers the same as a deletion.
#[tauri::command]
async fn forget_credential(deployment: String) -> bool {
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

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            keychain_reachable,
            keep_credential,
            read_credential,
            forget_credential
        ])
        .run(tauri::generate_context!())
        .expect("The MailFathom desktop shell failed to start.");
}
