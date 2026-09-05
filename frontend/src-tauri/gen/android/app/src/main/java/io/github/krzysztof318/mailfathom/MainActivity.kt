package io.github.krzysztof318.mailfathom

import android.os.Bundle
import androidx.activity.enableEdgeToEdge
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat

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
    keepThePageClearOfTheSystemBars()
  }

  /**
   * Pads the activity's content by whatever the status bar, the navigation bar, a display cutout and the keyboard
   * occupy, so the page is laid out in the space actually left to it.
   *
   * The client already asks for this the way the web platform offers it — `viewport-fit=cover` on the document and
   * `env(safe-area-inset-*)` behind the `--spacing-safe-*` tokens in `Client.App/src/styles.css`. Android's WebView
   * does not answer: it reports those insets for a display cutout alone and never for the system bars, so on a window
   * that `enableEdgeToEdge` above made full-bleed every one of them reads `0px` while the WebView spans the whole
   * display. What that costs is the top of whichever screen is open — the mail space's selection bar and its toolbar
   * sit under the clock, and the bottom navigation sits under the gesture handle.
   *
   * Answering it here rather than in the page is what keeps the other two heads out of it. Nothing in `frontend/src/`
   * learns that a third head exists, and a desktop or web window keeps reading the same tokens for the case they were
   * written for.
   *
   * The insets are consumed rather than passed on, because this view is the root the page is laid out inside and
   * anything below it would be padding a second time for the same bars.
   *
   * What padding costs is the strip behind each bar, which the window's own background paints rather than the page —
   * so a head whose theme is set against the system's shows the system's colour there. Lifting that means handing the
   * measured values to the page as the `--spacing-safe-*` tokens instead of padding at all, which needs a reference to
   * the WebView and a first delivery that cannot race the load. It is worth doing when the strip is worth it;
   * a nightly head drawing the right screens is worth more.
   */
  private fun keepThePageClearOfTheSystemBars() {
    ViewCompat.setOnApplyWindowInsetsListener(findViewById(android.R.id.content)) { view, insets ->
      val occupied = insets.getInsets(
        WindowInsetsCompat.Type.systemBars() or
          WindowInsetsCompat.Type.displayCutout() or
          WindowInsetsCompat.Type.ime(),
      )
      view.setPadding(occupied.left, occupied.top, occupied.right, occupied.bottom)

      WindowInsetsCompat.CONSUMED
    }
  }
}
