// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The desktop head, and the whole of it. The shell owns the window, the application identity, and the one thing the web
// head has no equivalent of: the operating system's own credential store. Every other behaviour the client has belongs
// to the bundle it wraps, which is what keeps a screen one screen across both heads.
//
// Four of the five commands below are what ADR 0023 decided this shell would offer, and they are the application's only
// reach into Rust that this repository wrote. They are commands this application defines rather than a plugin's, so the
// capability system does not gate them and no `capabilities/` file names them; what does reach them is the Tauri API
// the WebView is given through `withGlobalTauri` in `tauri.conf.json`, which is why this shell pins no JavaScript
// binding of its own.
//
// Each is `async` for one reason: Tauri runs a synchronous command on the main thread, and every one of these ends in a
// blocking call to something outside the process — a credential store, which on Linux is a D-Bus round trip that waits
// while a locked keyring asks its owner to unlock it, or a file on a disk that may be a network mount. Run there, that
// call freezes the window rather than the request; run on the async runtime, it occupies a worker and the application
// keeps painting.
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
use std::collections::HashMap;
use tauri::Manager;

/// The service every entry is written under, beside the deployment address the credential was given for.
const CREDENTIAL_SERVICE: &str = "MailFathom";

/// The file an operator writes beside the application's own configuration, in the directory this platform gives it.
const CONFIGURATION_FILE: &str = "client.conf";

/// The most of that file this shell reads. It is a handful of `key = value` lines, and anything past this is not one.
const LONGEST_CONFIGURATION_FILE: u64 = 64 * 1024;

/// What the two settings are called on the way across, which is what the application reads them back by.
const SERVICE_ADDRESS: &str = "serviceAddress";
const PERMIT_CLEAR_TEXT: &str = "permitClearText";

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

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            keychain_reachable,
            keep_credential,
            read_credential,
            forget_credential,
            client_configuration
        ])
        .run(tauri::generate_context!())
        .expect("The MailFathom desktop shell failed to start.");
}
