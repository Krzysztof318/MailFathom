// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Raising the system notification, and the half of it a plugin cannot do: hearing that somebody clicked one.
//
// `tauri-plugin-notification` stays registered beside this and still answers the permission question, because that is
// the one part of the operation whose answer differs between heads and the part the phone will need. What it cannot do
// is report a click on a desktop: its `notify` hands the notification to `notify_rust` on a spawned task and drops the
// handle, and the crate emits no event of any kind on Windows, macOS, or Linux — the `onAction` channel its JavaScript
// binding publishes belongs to the mobile plugin. A click therefore arrives nowhere, and nothing above the shell can
// fix that, because the handle a click arrives on lives and dies inside that one call.
//
// So this module raises the notification itself, keeps the handle, and waits on it. It is the same crate the plugin
// would have used, reached directly, which is why the client's notification looks no different for it.
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

/// Raises one notification saying the sentence given, and answers a click on it by bringing the window forward.
///
/// It returns as soon as the notification is on its way, exactly as the plugin's own command did, because raising
/// answers nothing a caller could act on: what the application learns from this operation is that permission stood.
///
/// The waiting runs on a thread of this shell's own rather than on the async runtime. `wait_for_response` blocks from
/// the moment the notification is shown until somebody acts on it or the operating system closes it, which is minutes
/// rather than milliseconds — long enough that occupying one of a bounded pool's workers with it would eventually
/// starve every other blocking call the process makes. A thread per notification is bounded by the same thing that
/// bounds the notifications themselves: the client raises one per arrival and only while nobody is looking at the
/// window.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub fn raise(app: tauri::AppHandle, said: String) {
    std::thread::spawn(move || {
        let mut notification = notify_rust::Notification::new();

        notification.summary(&said).auto_icon();

        // What makes a click reportable on Linux at all. The Freedesktop specification treats `default` as the
        // notification itself rather than a button drawn on it, and a server invokes it only where the notification
        // declared it — so a notification without this line is dismissed silently however it is clicked. The other two
        // platforms report a click on the body without being asked and ignore this.
        notification.action("default", "default");

        // Both of these are the plugin's own, and they are here for the reason the rest of this module is: replacing
        // `notify` means replacing everything it did. A packaged Windows application's toast is delivered against the
        // identity its installer registered, and an unpackaged one has none to name; macOS delivers against a bundle
        // identifier, and a development run has the terminal's rather than this application's.
        #[cfg(windows)]
        if !tauri::is_dev() {
            notification.app_id(&app.config().identifier);
        }

        #[cfg(target_os = "macos")]
        let _ = notify_rust::set_application(if tauri::is_dev() {
            "com.apple.Terminal"
        } else {
            &app.config().identifier
        });

        let Ok(shown) = notification.show() else {
            return;
        };

        let _ = shown.wait_for_response(|answered: &notify_rust::NotificationResponse| {
            // Everything that is not the notification closing is somebody acting on it, which is the way round that
            // keeps a dismissal from raising a window: a close reason is the only answer the operating system gives
            // for a notification nobody chose, and each platform spells activation differently — the body click XDG
            // reports as its `default` action, the activation Windows reports as an action of its own name.
            if !matches!(answered, notify_rust::NotificationResponse::Closed(_)) {
                bring_forward(&app);
            }
        });
    });
}

/// What a head linking nothing to raise one does, which is nothing at all.
///
/// The phone is that head today and the application above knows it: no notification plugin is linked on a mobile
/// target, so the operation reports itself unoffered and nothing ever reaches this. #1616 is where the phone raises its
/// own, through the plugin's mobile channel, which reports a click already and needs none of the above.
#[cfg(any(target_os = "android", target_os = "ios"))]
pub fn raise(_app: tauri::AppHandle, _said: String) {}

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
