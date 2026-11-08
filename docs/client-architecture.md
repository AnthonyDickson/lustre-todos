# Client Architecture

Gleam/Lustre SPA with nested MVU. All I/O goes through a custom `Effect` type
with a single interpreter — `update` functions stay pure.

## Layout

Modules every page uses live in `lustre_todos/shared/`. Each page is a folder,
`lustre_todos/<name>_page/`, holding the page module and the types and codecs
only it uses. Pages never import each other; a module moves to `shared/` when a
second page needs it. Tests mirror the source layout.

## Nested MVU

Two layers: `app.gleam` (shell and routing) delegates to the current page, such
as `todo_page/todo_page.gleam`. A page's `update` returns its model, its
effects, and a list of `OutMsg` values signalling app-level concerns to the
shell. The shell wraps the page's messages in its own `Msg`, maps the page's
effects with `effect.map`, and folds the out messages into its own model and
effects.

### App-level concerns: toasts

Toasts are an app-level concern, not a page concern. Pages don't own toast state
or markup — they emit `out_msg.PageRequestedToast(...)` (usually through the
`out_msg.info_toast` and `error_toast` helpers) and the shell owns rendering,
stacking, and dismissal:

- **Single placement and style** — the toast stack lives in one place
  (`toast.view_with_container`), so every page gets consistent UI.
- **Survives page changes** — toasts live in `app.gleam`'s `Model`, so
  navigating away doesn't destroy them.
- **One dismiss path** — auto-dismiss is scheduled by the shell with
  `effect.After(delay, ToastDismissed(id))`; manual dismissal is a single
  `ToastDismissed` message.

The requester still decides what a toast says (`title`, `body`), how severe it
is (`level: ToastLevel` — `Info`/`Warning`/`Error`, which drives border colour
and ARIA role), and whether it auto-dismisses (`dismiss_after_ms`). The shell
only renders.

The cost is wiring: every `OutMsg` variant must be handled in `map_out_msg`, and
every child `update` returns the extra list. The tradeoff pays off for anything
that should look and behave identically across the app — template consumers
signal the shell by adding a variant to `shared/out_msg.gleam`, never by
rendering their own UI.

## Effect System

`update` returns pure data — a single `effect.run` interprets effects:

| Effect               | Side effect                            |
| -------------------- | -------------------------------------- |
| `HttpRequest`        | `gleam_fetch` → typed callback         |
| `LoadFromStore`      | `window.localStorage.getItem`          |
| `SaveToStore`        | `window.localStorage.setItem`          |
| `LogError`           | Write to the console's error stream    |
| `Redirect`           | `window.location.assign` (a page load) |
| `SetTitle`           | `document.title`                       |
| `After(delay, msg)`  | Dispatch `msg` after `delay` ms        |
| `InitRouting`        | Click/popstate interception, first URL |
| `PushUrl/ReplaceUrl` | `history.pushState` / `replaceState`   |
| `Message(msg)`       | Dispatch `msg`                         |
| `Batch([...])`       | Run multiple effects                   |
| `NoEffect`           | No-op                                  |

### Bridging into Lustre

The shell wraps the inspectable `Effect` in Lustre's opaque type:

```gleam
fn update_with_effect(model, msg) -> #(Model, lustre_effect.Effect(Msg)) {
  let #(new_model, effect) = update(model, msg)
  #(new_model, lustre_effect.from(fn(dispatch) { effect.run(effect, dispatch) }))
}
```

### HTTP Helpers

Thin per-method constructors in `shared/effect.gleam` delegate to
`http_effect.send`:

```gleam
effect.get("/api/todos", callback)
effect.post(url, body, callback)
effect.delete(url, callback)
```

Requests have no timeout unless one is set with `effect.with_timeout(ms)`; the
transport then aborts the request and the callback gets a `NetworkError`. The
todo page bounds every request, so a hung optimistic write is rolled back.

## Remote Data

Data a page fetches is a `Remote(a)` (`shared/remote.gleam`): `Loading`,
`Loaded(a)`, or `Failed`. The view shows each state, so a page never renders an
empty list while it waits or after a fetch fails; `Failed` offers a retry.
`remote.map` changes loaded data and drops a change made while nothing is
loaded, and `remote.unwrap` reads it with a default.

The todo page also keeps a copy of the list in `localStorage`. The copy shows
while the server responds but never replaces the server's list, and a failed
fetch with the copy showing only warns. Nothing is saved while the list is not
loaded, so a slow or failed fetch cannot wipe the copy. Drop the copy for data
that must not be left in a shared browser.

## Guard Helpers

`shared/guard.gleam` provides `use`-compatible early-return for `Option` and
`Result`. `some` and `ok` take the fallback value; `some_lazy` and `ok_lazy`
take a function, for a fallback that is costly to build or needs the error:

```gleam
use value <- guard.some(in: my_option, else_return: fallback)
use value <- guard.ok_lazy(in: my_result, else_return: fn(e) { handle_error(e) })
```

## Routing

No router library — custom navigation effects via browser APIs.
`shared/route.gleam` is the typed list of routes: `from_string` parses a URL
(ignoring its query and fragment) and `to_string` builds the links, so paths are
written once. An unrecognised path is `NotFound`.

`shared/api_route.gleam` does the same for the server's API: requests build
their URLs from it rather than from strings, so each API path is written once.

`app.gleam` holds the current page as a `Page` union, one case per page. On a
URL change it sets the title and either updates the page it is already on
(keeping its state, e.g. the todo filter changing) or starts the new page with
its `init`. A page message that arrives after its page has gone is dropped. To
add a page: add its routes, a `Page` case, a `Msg` wrapper, and a `change_page`
arm.

## Session Expiry

The shell rewrites every HTTP effect so a `401` response dispatches its own
`SessionExpired` instead of reaching the page. It then redirects to `/login`
with the current URL as the return URL, so the user comes back to the same page.
Pages never handle `401` themselves. `/login` and `/logout` are server
endpoints, not SPA routes (`shared/auth_route.gleam`): views reach them through
forms or `effect.Redirect`, since the router would catch a link. The
`/signed-out` page makes no API request, so it does not send the user straight
back to the provider.

## Client Tests

Pure unit tests in `client/test/` using gleeunit. No browser or DOM — tests call
`update` with a `Model` and `Msg`, then assert the returned `Model` and inspect
the `Effect` payload.

```bash
just client-test
```

## When to add a page

| Condition                               | Pattern                 |
| --------------------------------------- | ----------------------- |
| Single feature, one concern             | Add to existing page    |
| New feature with independent state      | New page module         |
| Feature shares state with existing page | Extend existing page    |
| Global state (auth, theme, user prefs)  | Extend shell model      |
| Transient app-wide UI (toasts, banners) | Emit `OutMsg` from page |
