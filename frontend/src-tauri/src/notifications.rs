// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Raising the system notification, and answering a click on one.
//
// Both halves are `tauri-plugin-notification`'s. It raises the notification, and it reports a click on one to whatever
// registered with its `on_action` — on Windows, macOS, and Linux as on the phone — so this module holds no handle,
// starts no thread, and names no notification crate of its own. What is left here is the one thing a plugin cannot
// know: what this application does about a click, which is to bring its window forward and tell the bundle.
//
// It stays a command of this shell rather than the plugin's own `notify` reached from the bundle, because the answer to
// a click is given here either way. A window is raised from Rust or through core window permissions granted to the
// webview, and `capabilities/` grants no core permission at all — so the bundle keeps asking this shell for one thing
// and the capability keeps naming the two permission commands and nothing else.
//
// **What is said is still a count and a kind and nothing else.** Nothing here composes a sentence: the application
// hands over one line that `notifications/arrivalCounts.ts` already reduced, and a click changes what happens after a
// notification rather than what it says.

/// What the bundle hears when somebody acted on one, which is the whole of what crosses back.
///
/// It carries no payload for the same reason the notification carries no mail: which arrival was clicked is a question
/// about mail, and the centre the click opens is where the answer already is.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub const ACTED_ON: &str = "system-notification-acted-on";

/// The window this shell owns, named in `tauri.conf.json` and the one a click brings back.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
const WINDOW: &str = "main";

/// Starts answering a click on a notification, which has to happen before the first one is raised.
///
/// The order is the plugin's rule rather than a preference: it tracks a notification only where something was
/// listening when it was shown, and one raised before that reports nothing however it is clicked. So this is called
/// once while the shell is set up, and every notification `raise` below shows is one whose click arrives here. Every
/// action is answered alike, because this application registers no action of its own — the only one a notification
/// carries is the click on its body.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub fn answer_clicks(app: &tauri::AppHandle) {
    use tauri_plugin_notification::NotificationExt;

    let shell = app.clone();

    let _ = app.notification().on_action(move |_| bring_forward(&shell));
}

/// Raises one notification saying the sentence given.
///
/// It returns as soon as the notification is on its way, because raising answers nothing a caller could act on: the
/// plugin dispatches it on a task of its own, and what the application learns from this operation is that permission
/// stood. A notification the operating system would not show is dropped for the same reason.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub fn raise(app: &tauri::AppHandle, said: String) {
    use tauri_plugin_notification::NotificationExt;

    let _ = app.notification().builder().title(said).show();
}

/// What a head linking nothing to raise one does, which is nothing at all.
///
/// The phone is that head today and the application above knows it: no notification plugin is linked on a mobile
/// target, so the operation reports itself unoffered and nothing ever reaches this. #1616 is where the phone raises its
/// own, through the same plugin's mobile side.
#[cfg(any(target_os = "android", target_os = "ios"))]
pub fn raise(_app: &tauri::AppHandle, _said: String) {}

/// Puts the window in front of whatever was covering it, and tells the bundle a notification was acted on.
///
/// Three calls rather than one because a window can be behind another, hidden, or minimised, and each is a different
/// state: showing a window that is merely covered changes nothing, and focusing a minimised one raises nothing. The
/// event goes out whether or not any of them worked, because opening the notification centre is the half of the
/// promise that does not depend on a window manager honouring a focus request.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
fn bring_forward(app: &tauri::AppHandle) {
    use tauri::{Emitter, Manager};

    if let Some(window) = app.get_webview_window(WINDOW) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }

    let _ = app.emit(ACTED_ON, ());
}
