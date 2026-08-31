// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// `tauri::generate_context!` in `src/main.rs` expands to what this generates: the parsed configuration, the icons, and
// the access-control list. Without it the crate does not compile rather than starting without them.

fn main() {
    tauri_build::build();
}
