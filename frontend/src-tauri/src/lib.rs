// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The shell both native heads are, and the whole of it. It owns the window, the application identity, and the one thing
// the web head has no equivalent of: the operating system's own credential store. Every other behaviour the client has
// belongs to the bundle it wraps, which is what keeps a screen one screen across every head.
//
// Four of the six commands below are what ADR 0023 decided this shell would offer, and they are part of the
// application's only reach into Rust that this repository wrote. What answers them lives in `credentials.rs` beside
// this file, in two implementations selected by target — the desktop keychain and, per ADR 0027, the Android Keystore
// — because that is the one operation whose *answer* differs between the heads rather than only its mechanism. They
// are commands this application defines rather than a plugin's, so the capability system does not gate them and no
// `capabilities/` file names them; what does reach them is the Tauri API the WebView is given through `withGlobalTauri`
// in `tauri.conf.json`, which is why this shell pins no JavaScript binding of its own.
//
// Each of those four and the fifth beside them is `async` for one reason: Tauri runs a synchronous command on the main
// thread, and every one of them ends in a blocking call to something outside the process — a credential store, which
// on Linux is a D-Bus round trip that waits while a locked keyring asks its owner to unlock it, or a file on a disk
// that may be a network mount. Run there, that call freezes the window rather than the request; run on the async
// runtime, it occupies a worker and the application keeps painting.
//
// None of the credential commands reports why it failed. Everything they could report is about a password, and a client
// that is told nothing simply asks for it again — which is the same outcome a browser refusing storage produces on the
// other head.
//
// The fifth is `client_configuration`, and it is the one place this shell reads anything an operator wrote. It resolves
// nothing: it reads the three places a deployment states a setting and hands back what each of them said, as text.
// Which of the three wins, what a value has to be to be one, and what a contradiction between two of them costs are all
// the application's, in `shellOperations/configuredConnection.ts` and `deployment/adoptedDeployment.ts`, because they
// are the same decisions on either head and a rule written twice is a rule two heads eventually disagree about.
//
// The sixth is `raise_notification`, and it is the one command here that exists because a plugin could not do the whole
// of what it is for. It is not `async` and needs nothing from the runtime: it hands the waiting to a thread of its own
// and returns, which `notifications.rs` beside this file explains along with why the waiting is what this shell had to
// take over.
//
// Two plugins are registered beside them. The opener answers the first operation the bundle asks of a shell — handing a
// link a reader followed to the system browser, per ADR 0024 — and the notification plugin answers half of the second,
// which is whether raising one is permitted at all. Both are plugins' commands, so both are gated:
// `capabilities/open-a-link.json` narrows what the webview may ask of the opener to the three schemes a message body
// may carry, and `capabilities/raise-a-notification.json` narrows the notification plugin to the two permission
// commands the application still calls, on the desktop targets alone, rather than taking the plugin's own default set.
// No core capability is granted either.
//
// The notification plugin is also the one thing this crate does not link everywhere. `Cargo.toml` states it per target
// and `run` below registers it under the same condition, for a reason about the Android project's locked dependency
// graph that is written out where the pin is. Nothing above the shell learns that: the application asks whether the
// operation is offered, and a head that linked no plugin answers no differently from a browser that has no shell at
// all.
//
// This is a library rather than the binary it was, and `src/main.rs` beside it is now three lines calling `run` below.
// The split is what an Android head needs and the only thing about this file that is Android's: an application there is
// started by the platform through a JNI entry point in a shared object rather than by an operating system running an
// executable, so the whole shell has to be reachable from a library. `#[tauri::mobile_entry_point]` is what generates
// that entry point, it attaches to a function in a library target, and it expands to nothing off a mobile target — so
// the desktop head builds from exactly the same `run` the phone starts. ADR 0027 is the record that says an Android
// head exists here at all, and ADR 0021's position that nothing in the *application* branches on a platform is
// untouched by a shell that carries one attribute.

mod credentials;
mod notifications;

use std::collections::HashMap;
use tauri::Manager;

/// The file an operator writes beside the application's own configuration, in the directory this platform gives it.
const CONFIGURATION_FILE: &str = "client.conf";

/// The most of that file this shell reads. It is a handful of `key = value` lines, and anything past this is not one.
const LONGEST_CONFIGURATION_FILE: u64 = 64 * 1024;

/// What the two settings are called on the way across, which is what the application reads them back by.
const SERVICE_ADDRESS: &str = "serviceAddress";
const PERMIT_CLEAR_TEXT: &str = "permitClearText";

/// Where this shell will keep the credential, which is the sentence the sign-in screen renders before anybody types.
///
/// It is the arrangement rather than a fact about the machine, because the same fact resolves differently per head and
/// only the shell knows which head it is; `credentials.rs` holds the reasoning.
#[tauri::command]
async fn credential_arrangement() -> &'static str {
    credentials::arrangement().await
}

/// Keeps the finished header value for one deployment, answering whether it was kept.
#[tauri::command]
async fn keep_credential(deployment: String, authorization: String) -> bool {
    credentials::keep(deployment, authorization).await
}

/// The header value kept for one deployment, or nothing where none was kept or the store would not answer.
#[tauri::command]
async fn read_credential(deployment: String) -> Option<String> {
    credentials::read(deployment).await
}

/// Deletes what was kept for one deployment, which is what sign-out does and the only thing that removes it.
#[tauri::command]
async fn forget_credential(deployment: String) -> bool {
    credentials::forget(deployment).await
}

/// What each of the three places an operator configures this client from said, whether or not any of it is usable.
///
/// A source that stated nothing answers with an empty map rather than being absent, so the application reads the same
/// three names on every machine and a missing one is never a shape it has to guess at. Nothing is validated, trimmed
/// past its surrounding whitespace, or preferred over anything else here — the value crosses as the operator wrote it,
/// and a deployment that wrote something that is not an address learns so from the screen rather than from this shell
/// quietly dropping it.
#[tauri::command]
async fn client_configuration(app: tauri::AppHandle) -> HashMap<&'static str, HashMap<&'static str, String>> {
    HashMap::from([
        ("commandLine", from_command_line()),
        ("environment", from_environment()),
        ("configurationFile", from_configuration_file(&app)),
    ])
}

/// What the arguments this application was started with stated, in the `--flag=value` form the service's own take.
///
/// A later argument wins over an earlier one, which is what a shortcut edited by hand and a wrapper script appending to
/// it between them produce. `--permit-clear-text` alone is the permission granted, because a flag that has to be
/// written `=true` to mean anything is a flag nobody writes correctly the first time.
fn from_command_line() -> HashMap<&'static str, String> {
    let mut stated = HashMap::new();

    for argument in std::env::args().skip(1) {
        if let Some(value) = argument.strip_prefix("--service-address=") {
            stated.insert(SERVICE_ADDRESS, value.to_owned());
        } else if let Some(value) = argument.strip_prefix("--permit-clear-text=") {
            stated.insert(PERMIT_CLEAR_TEXT, value.to_owned());
        } else if argument == "--permit-clear-text" {
            stated.insert(PERMIT_CLEAR_TEXT, "true".to_owned());
        }
    }

    stated
}

/// What the environment this application was started in stated.
fn from_environment() -> HashMap<&'static str, String> {
    let mut stated = HashMap::new();

    for (variable, setting) in [
        ("MAILFATHOM_SERVICE_ADDRESS", SERVICE_ADDRESS),
        ("MAILFATHOM_PERMIT_CLEAR_TEXT", PERMIT_CLEAR_TEXT),
    ] {
        if let Ok(value) = std::env::var(variable) {
            stated.insert(setting, value);
        }
    }

    stated
}

/// What the file beside this application's own configuration stated, or nothing where there is no such file.
///
/// `key = value` lines, `#` comments, and nothing else: the format is what a person edits in one sitting rather than
/// what a parser can express, and it costs this shell no dependency to read. A key it does not know is skipped rather
/// than refused, because a file an operator shares between two versions of the client is the ordinary case.
///
/// The read is bounded. The file is the operator's own, so this is not the trust boundary a response body is, but a
/// path anything on the machine may write to is still one worth refusing to read a gigabyte of.
fn from_configuration_file(app: &tauri::AppHandle) -> HashMap<&'static str, String> {
    let mut stated = HashMap::new();

    let Ok(directory) = app.path().app_config_dir() else {
        return stated;
    };

    let path = directory.join(CONFIGURATION_FILE);

    match std::fs::metadata(&path) {
        Ok(found) if found.len() <= LONGEST_CONFIGURATION_FILE => {}
        _ => return stated,
    }

    let Ok(text) = std::fs::read_to_string(&path) else {
        return stated;
    };

    for line in text.lines() {
        let Some((key, value)) = line.trim().split_once('=') else {
            continue;
        };

        match key.trim() {
            "service-address" => stated.insert(SERVICE_ADDRESS, value.trim().to_owned()),
            "permit-clear-text" => stated.insert(PERMIT_CLEAR_TEXT, value.trim().to_owned()),
            _ => None,
        };
    }

    stated
}

/// Raises one system notification saying the sentence given, and answers a click on it by bringing the window forward.
///
/// It answers nothing, because raising one answers nothing a caller could act on: whether anything appeared is the
/// operating system's business from that moment, and what the application needed to know — that permission stood — the
/// plugin's own permission commands already told it.
#[tauri::command]
fn raise_notification(app: tauri::AppHandle, said: String) {
    notifications::raise(app, said);
}

/// Starts the shell. Every head enters here: the desktop binary calls it from `main`, and Android's generated entry
/// point calls it from the activity the platform started.
#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let shell = tauri::Builder::default().plugin(tauri_plugin_opener::init());

    // The Android head's credential store is a Kotlin class in its own application module, and a plugin is how Tauri
    // reaches one. Nothing registers on the desktop, where the same four commands answer out of the `keyring` crate.
    #[cfg(target_os = "android")]
    let shell = shell.plugin(credentials::registration());

    // The notification plugin is not linked on a mobile target at all — `Cargo.toml` carries why, and the phone's own
    // notification is #1616 — so there is nothing to register there. Where it is linked the registration is
    // unconditional, and the application above asks whether the operation is offered rather than which head answered.
    #[cfg(not(any(target_os = "android", target_os = "ios")))]
    let shell = shell.plugin(tauri_plugin_notification::init());

    shell
        .invoke_handler(tauri::generate_handler![
            credential_arrangement,
            keep_credential,
            read_credential,
            forget_credential,
            client_configuration,
            raise_notification
        ])
        .run(tauri::generate_context!())
        .expect("The MailFathom shell failed to start.");
}
