// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The desktop head, and the whole of it. The shell owns the window and the application identity; every behaviour the
// client has belongs to the bundle it wraps, which is what keeps a screen one screen across both heads.
//
// Nothing here registers a command or a plugin, so the application source calls into Rust nowhere and the webview is
// granted no capability. A `capabilities/` directory is what the first command would need, and adding one before there
// is a command to permit would grant reach nothing asked for.
//
// There is no library target beside this one. The template's split exists so that `tauri android init` and
// `tauri ios init` have a `mobile_entry_point` to attach to, and ADR 0021 supports no mobile head: the tree stays
// reachable for one because the application source carries no platform branch, not because this crate is shaped for a
// target nothing builds.

// A Windows release opens a console window beside the application without this, because a Rust binary is a console
// subsystem executable by default. A debug build keeps it, which is where the WebView's own diagnostics are read.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("The MailFathom desktop shell failed to start.");
}
