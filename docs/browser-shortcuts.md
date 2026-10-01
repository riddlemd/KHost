# Browser shortcuts in KHost's windows

`src/KHost.UserInterface/wwwroot/js/browser-keys.js` is the one list of browser and webview shortcuts
a KHost page cancels. The console loads it on every surface; LocalScreen embeds the same file into
its player page. It cancels with `preventDefault` in a capture-phase `keydown` and never stops
propagation, so the page's own handlers still see every key.

Surfaces:

- **Native** — the Photino console window and the LocalScreen window (`<html data-kh-surface="native">`).
- **Tab** — the headless console in an ordinary browser. The browser keeps what is its own.

Devtools chords are cancelled by the page only in a Release native console
(`data-kh-devtools="block"`, the same line as `shell-lockdown.js`). Both Photino windows already
turn devtools off in Release builds through `SetDevToolsEnabled`, so a Debug build keeps F12 and the
inspector, and a browser tab's devtools are the browser user's own.

| Shortcut | What it does to a running show | Decision | Surface |
|---|---|---|---|
| Mod+R, Shift+Mod+R, F5, Ctrl+F5 | Drops the Blazor circuit mid-show; reloads the screen's player, which does not reconnect | Cancel | Native, tab |
| Mod+F, F3, Mod+G, Shift+Mod+G | Opens a find bar over the console | Cancel | Native, tab |
| Mod+P | Opens the print dialog over the show | Cancel | Native, tab |
| Mod+S | Opens a save dialog for the page | Cancel | Native, tab |
| Mod + `+` / `-` / `=` / `0`, Ctrl+wheel, pinch | Zooms the console out of its layout | Cancel (Cmd too, and by key position) | Native, tab |
| Mod+A outside a text field | Highlights the whole console | Cancel; in a field it still selects the text | Native |
| Alt+←/→ | Back/forward (Windows, Linux) | Cancel; in a text field only off a Mac, where Option+arrow is a word jump. The song search box owns it (switch search source) | Native, tab |
| Mod+←/→ outside a text field, Mod+[ / ] | Back/forward (Chrome and Safari on a Mac) | Cancel; Mod+arrows in a field still move the caret | Native, tab |
| Backspace outside a text field | Back, in webviews that still bind it | Cancel, except in a keyboard list (removes the row) and with a modifier (Mod+Backspace stops the song) | Native, tab |
| F12, Mod+Shift+I/J/C, Cmd+Option+I/J/C, Mod+U | Opens devtools or the page source | Cancel in a Release native console; Debug keeps them | Native (Release) |
| Mod+N, Mod+T, Mod+W, Shift+Mod+T/N/W | New/close window or tab | Left alone: the browser reserves them and a page cannot cancel them. A webview has no tabs to open | Neither |
| Mod+C, V, X, Z, Y, Shift+Mod+Z | Editing | Never touched | Neither |

Notes:

- **WebView2 cannot be told to drop its accelerators from here.** `AreBrowserAcceleratorKeysEnabled`
  is the setting, and Photino.NET 4.0.16 does not expose it: Photino.Native sets only the context
  menu, devtools and a few script switches on `ICoreWebView2Settings`, and its
  `SetBrowserControlInitParameters` reaches Chromium's command line, which has no switch for it.
  WebView2 hands the page each accelerator key before acting on it, so the page-level block is what
  stands in.
- The mouse's back/forward buttons, the swipe-back gesture and the context menu are
  `shell-lockdown.js`'s, which loads only in a Release native console.
- The decisions are checked against stub key events only. Whether `preventDefault` beats each
  accelerator has not been tried by hand in WebView2, the Windows kiosk, WebKitGTK, or Safari and
  WKWebView (Cmd+1..9 included).
