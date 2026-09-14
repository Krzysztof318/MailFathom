// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Where an OAuth sign-in comes back to on this head, which is the second thing the web head has an answer for and this
// one does not. A page signs in by leaving its own origin and being redirected back to it; a WebView has no origin an
// authorization server would ever redirect to, and putting the authorization screen *inside* the WebView would be the
// client asking somebody to type their company password into a window the client itself painted — which is the one
// thing every recommendation on this agrees a native application must not do.
//
// So the person authorizes in their own browser, and the redirect comes back to a loopback address this shell is
// listening on. That is RFC 8252's recommendation for a native application, and it is already how `mfctl login` works
// in this repository — one arrangement for both, so an operator registers the same kind of address for each and learns
// one thing rather than two.
//
// Loopback only. The listener is bound to `127.0.0.1` rather than to every address, so the only thing that can deliver
// a code here is a browser on this machine; the port is fixed because a redirect URI is registered at the authorization
// server exactly as written, and a port chosen at run time could not be registered in advance.
//
// It carries no capability file of its own, and nothing here is a plugin's command. That is also why the address is
// checked here: `capabilities/open-a-link.json` narrows what the *webview* may ask the opener for, and this call is
// made from Rust inside a command the capability system does not gate, so that file's scheme scope never applies on
// this path. Opening it is done here rather than from the page so that the listener is bound before the browser is
// started — a browser that reached the redirect first would find nothing answering.

/// Where an authorization server sends the person back, which an operator registers exactly as written.
///
/// `127.0.0.1` rather than `localhost` for the reason `mfctl` writes the same address that way: a name resolving to
/// both an IPv4 and an IPv6 address is a redirect that arrives at whichever the browser picked, and a listener bound to
/// one of them. The port is this head's own rather than the one `mfctl` uses, so a person signing in to the client
/// while a login is running in a terminal does not find the port taken.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub const REDIRECT_URI: &str = "http://127.0.0.1:8766/";

/// Reports where this head receives an authorization redirect, or nothing where it receives none.
///
/// A head with no answer offers the client no OAuth sign-in at all, which is what the Android head is until it has a
/// redirect arrangement of its own: the client asks the operation rather than the platform, so a screen there simply
/// draws no provider control instead of drawing one that goes nowhere.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub fn redirect_uri() -> Option<&'static str> {
    Some(REDIRECT_URI)
}

#[cfg(any(target_os = "android", target_os = "ios"))]
pub fn redirect_uri() -> Option<&'static str> {
    None
}

#[cfg(any(target_os = "android", target_os = "ios"))]
pub async fn follow(_address: String) -> Option<String> {
    None
}

#[cfg(any(target_os = "android", target_os = "ios"))]
pub fn abandon() {}

#[cfg(not(any(target_os = "android", target_os = "ios")))]
pub use desktop::{abandon, follow};

#[cfg(not(any(target_os = "android", target_os = "ios")))]
mod desktop {
    use std::io::{Read, Write};
    use std::net::TcpListener;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::time::{Duration, Instant};

    /// The address the listener binds, which is the authority half of [`super::REDIRECT_URI`].
    const BIND_ADDRESS: &str = "127.0.0.1:8766";

    /// How long the listener waits for the browser to come back before it gives the port up.
    ///
    /// It is a person authenticating rather than a machine answering, so the wait is minutes: a first sign-in of the
    /// day carries a password, a second factor, and a consent screen. What the bound is for is a browser nobody ever
    /// came back from — a tab closed on another machine, a laptop suspended mid-sign-in — where nothing in the
    /// application is left to say so. A sign-in the person abandoned in front of the client is not that case and does
    /// not wait this out: [`abandon`] is what the screen calls, and the port is back within one look.
    const LONGEST_WAIT: Duration = Duration::from_secs(300);

    /// How often the listener looks for a connection while it waits, which is what makes the bound above reachable.
    const BETWEEN_LOOKS: Duration = Duration::from_millis(100);

    /// The most of one request this reads. A redirect is a request line and headers; anything past this is not one.
    const LONGEST_REQUEST: usize = 16 * 1024;

    /// How long one connection has to send its request line once it is accepted.
    const LONGEST_READ: Duration = Duration::from_secs(5);

    /// Whether the sign-in being waited for has been given up on, which is what ends the wait before its deadline.
    ///
    /// One flag rather than one per attempt, because one attempt is all there can be: the port is what serializes them,
    /// and a second `follow` while one is waiting finds it bound and answers nothing. It is cleared by whichever
    /// attempt binds the port, so an abandonment can only end the wait it was asked about.
    static ABANDONED: AtomicBool = AtomicBool::new(false);

    /// Gives up on the hand-over being waited for, so the next attempt binds the port instead of finding it held.
    pub fn abandon() {
        ABANDONED.store(true, Ordering::Relaxed);
    }

    /// What the browser is left looking at, which is the only document this shell ever serves.
    const COMPLETION_PAGE: &str = concat!(
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>MailFathom</title></head>",
        "<body style=\"font-family: system-ui, sans-serif; margin: 4rem auto; max-width: 32rem;\">",
        "<p>You can close this tab and return to MailFathom.</p></body></html>"
    );

    /// The whole answer, with the length measured off the document rather than written beside it.
    fn completion_response() -> String {
        format!(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {}\r\n\r\n{COMPLETION_PAGE}",
            COMPLETION_PAGE.len()
        )
    }

    /// Hands the person to the authorization server and answers the query its redirect came back with.
    ///
    /// The listener is bound before the browser is opened, which is the whole reason this is one operation rather than
    /// two: a page that asked for the browser first and then asked for a listener would be racing the redirect.
    ///
    /// Nothing about what comes back is read here. The query travels to the application as the browser sent it, because
    /// which parameters an answer has to carry, which of them is a code and which a refusal, and what the state has to
    /// match are the client's contract rather than this shell's — and a shell that parsed them would be a second
    /// reading of the flow to keep in step with the first.
    pub async fn follow(address: String) -> Option<String> {
        let Ok(listener) = TcpListener::bind(BIND_ADDRESS) else {
            // Somebody is already signing in — another window of this application, or `mfctl` on the same port had it
            // been configured there. Answering nothing puts the person back on the sign-in screen rather than opening
            // a browser whose redirect would arrive at whatever is listening.
            return None;
        };

        ABANDONED.store(false, Ordering::Relaxed);

        // The address arrived over IPC from the WebView, and this call is not the one `capabilities/open-a-link.json`
        // narrows — that file scopes what the *page* may ask the plugin for, and this asks it from Rust. So the scheme
        // is checked here, and it is the one an authorization endpoint may be reached at: everything a discovery
        // document names is `https` by the time `Client.Backend` has read it, and handing the desktop's own opener
        // anything else would be this shell launching whatever a scheme is registered to.
        if !address.starts_with("https://") || tauri_plugin_opener::open_url(address, None::<&str>).is_err() {
            return None;
        }

        // The wait is a blocking one on a thread of its own rather than on the async runtime's, because it is minutes
        // long by design: run on a worker it would occupy one for the whole of somebody's sign-in.
        tauri::async_runtime::spawn_blocking(move || waited_for(&listener))
            .await
            .ok()
            .flatten()
    }

    /// Waits for the browser to arrive, and answers the query of the first request that carries one.
    ///
    /// A browser opening a redirect makes more than one request at it — a favicon, a speculative connection — so a
    /// request with no query is answered and the listener goes on waiting rather than treating it as the answer.
    fn waited_for(listener: &TcpListener) -> Option<String> {
        if listener.set_nonblocking(true).is_err() {
            return None;
        }

        let deadline = Instant::now() + LONGEST_WAIT;

        while Instant::now() < deadline {
            if ABANDONED.load(Ordering::Relaxed) {
                return None;
            }

            let Ok((mut connection, _)) = listener.accept() else {
                std::thread::sleep(BETWEEN_LOOKS);

                continue;
            };

            // Back to blocking for this one connection: the wait above is what the deadline governs, and a browser
            // that has connected is sending its request now or not at all, which the read timeout is what bounds.
            if connection.set_nonblocking(false).is_err()
                || connection.set_read_timeout(Some(LONGEST_READ)).is_err()
            {
                continue;
            }

            let mut request = [0_u8; LONGEST_REQUEST];
            let Ok(read) = connection.read(&mut request) else {
                continue;
            };

            let _ = connection.write_all(completion_response().as_bytes());
            let _ = connection.flush();

            if let Some(query) = query_of(&request[..read]) {
                return Some(query);
            }
        }

        None
    }

    /// The query of the request line, or nothing where the request carried no answer to the flow.
    ///
    /// The request line is `GET /path?query HTTP/1.1`, and only its middle field is read. It is bounded by the buffer
    /// above and by the space that ends the target, so nothing here walks past what one request line can be.
    ///
    /// Read lossily rather than fallibly: a byte no text can hold belongs to a header this never looks at, and
    /// refusing the whole request over one would leave somebody who has already authorized waiting out the deadline.
    ///
    /// A query is an answer only where it names `code` or `error`, which is the least this can ask and still be sure
    /// the wait is over: anything on this machine may reach a loopback port — another page, an extension probing it, a
    /// scanner — and a request carrying any query at all would otherwise end the wait, drop the listener, and leave the
    /// authorization server's real redirect arriving at a closed port after the person has already authorized. Which
    /// of the two it is, and what the state has to match, stay the client's reading rather than becoming a second one.
    fn query_of(request: &[u8]) -> Option<String> {
        let request = String::from_utf8_lossy(request);
        let line = request.lines().next()?;
        let target = line.split(' ').nth(1)?;
        let query = target.split_once('?')?.1;

        let answers = query
            .split('&')
            .any(|stated| matches!(stated.split('=').next(), Some("code" | "error")));

        answers.then(|| query.to_owned())
    }
}
