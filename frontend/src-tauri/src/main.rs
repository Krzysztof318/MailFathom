// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The desktop head's entry point, and nothing else. Everything the shell is lives in `lib.rs` beside this, because an
// Android application is started through a JNI entry point in a shared object rather than by running an executable, and
// a binary target is not reachable from one.

// A Windows release opens a console window beside the application without this, because a Rust binary is a console
// subsystem executable by default. A debug build keeps it, which is where the WebView's own diagnostics are read.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    mailfathom_desktop_lib::run();
}
