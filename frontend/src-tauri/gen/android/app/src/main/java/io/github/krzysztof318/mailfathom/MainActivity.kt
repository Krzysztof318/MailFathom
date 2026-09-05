package io.github.krzysztof318.mailfathom

import android.os.Bundle
import androidx.activity.enableEdgeToEdge

class MainActivity : TauriActivity() {
  /**
   * Hands the system back gesture to the page rather than to the activity, which is the whole of what this head does
   * about it. Tauri's own activity turns this off, so back finishes the application from wherever a reader happens to
   * be — a conversation, a drawer, an open dialog — and there is nothing the client could answer instead. Turned on,
   * the WebView consumes one session-history entry per press and only finishes the activity once there is none left,
   * which is exactly what a browser does with the same gesture.
   *
   * That is why nothing in `frontend/src/` branches on this head for it: the shell is configured to deliver back the
   * way every other head already delivers it, and `Client.App/src/shellOperations/backNavigation.ts` is the one
   * implementation all three share.
   */
  override val handleBackNavigation: Boolean = true

  override fun onCreate(savedInstanceState: Bundle?) {
    enableEdgeToEdge()
    super.onCreate(savedInstanceState)
  }
}
