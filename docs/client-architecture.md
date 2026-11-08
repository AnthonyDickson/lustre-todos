# Client Architecture

Gleam/Lustre SPA with nested MVU. All I/O goes through a custom `Effect` type
with a single interpreter — `update` functions stay pure.

## Layout

Modules every page uses live in `lustre_todos/shared/`. Each page is a folder,
`lustre_todos/<name>_page/`, holding the page module and the types and codecs
only it uses. Pages never import each other; a module moves to `shared/` when a
second page needs it. Tests mirror the source layout.

## Nested MVU

Two layers: `app.gleam` (shell + routing) delegates to `todo_page/todo_page.gleam`
(feature page). Child `update` functions return a third element — a list of
`OutMsg` values signalling app-level concerns to the shell:

```gleam
pub fn update(
  model: Model,
  msg: Msg,
) -> #(Model, effect.Effect(Msg), List(OutMsg)) {
```

The parent maps child effects through `effect.map` and folds the out
messages into its own model and effects with `with_out_msgs`:

```gleam
TodoPageMsg(inner_msg) -> {
  let #(inner_model, inner_effect, out_msgs) =
    todo_page.update_with_storage(model.todo_page, inner_msg)
  #(
    Model(..model, todo_page: inner_model),
    effect.map(inner_effect, TodoPageMsg),
  )
  |> with_out_msgs(out_msgs)
}
```

### App-level concerns: toasts

Toasts are an app-level concern, not a page concern. Pages don't own toast
state or markup — they emit `out_msg.PageRequestedToast(...)` and the shell
owns rendering, stacking, and dismissal:

- **Single placement and style** — the toast stack lives in one place
  (`toast.view_with_container`), so every page gets consistent UI.
- **Survives page changes** — toasts live in `app.gleam`'s `Model`, so
  navigating away doesn't destroy them.
- **One dismiss path** — auto-dismiss is scheduled by the shell with
  `effect.After(delay, ToastDismissed(id))`; manual dismissal is a single
  `ToastDismissed` message.

The requester still decides what a toast says (`title`, `body`), how severe
it is (`level: ToastLevel` — `Info`/`Warning`/`Error`, which drives border
colour and ARIA role), and whether it auto-dismisses (`dismiss_after_ms`).
The shell only renders.

The cost is wiring: every `OutMsg` variant must be handled in
`map_out_msg`, and every child `update` returns the extra list. The tradeoff
pays off for anything that should look and behave identically across the
app — template consumers signal the shell by adding a variant to
`out_msg.gleam`, never by rendering their own UI.

## Effect System

`update` returns pure data — a single `effect.run` interprets effects:

| Effect               | Side effect                             |
| -------------------- | --------------------------------------- |
| `HttpRequest`        | `gleam_fetch` → typed callback          |
| `LoadFromStore`      | `window.localStorage.getItem`           |
| `SaveToStore`        | `window.localStorage.setItem`           |
| `Redirect`           | `window.location.assign`                |
| `After(delay, msg)`  | Dispatch `msg` after `delay` ms         |
| `Navigate`           | Click/popstate interception + pushState |
| `PushUrl/ReplaceUrl` | `history.pushState` / `replaceState`    |
| `Batch([...])`       | Run multiple effects                    |
| `None`               | No-op                                   |

### Bridging into Lustre

The shell wraps the inspectable `Effect` in Lustre's opaque type:

```gleam
fn update_with_effect(model, msg) -> #(Model, lustre_effect.Effect(Msg)) {
  let #(new_model, effect) = update(model, msg)
  #(new_model, lustre_effect.from(fn(dispatch) { effect.run(effect, dispatch) }))
}
```

### HTTP Helpers

Thin per-method constructors in `effect.gleam` delegate to `http_effect.send`:

```gleam
effect.get("/api/todos", callback)
effect.post(url, body, "application/json", callback)
effect.delete(url, callback)
```

## Guard Helpers

`guard.gleam` provides `use`-compatible early-return for `Option` and `Result`:

```gleam
use value <- guard.some(in: my_option, else_return: fn() { fallback })
use value <- guard.ok(in: my_result, else_return: fn(e) { handle_error(e) })
```

## Routing

No router library — custom navigation effects via browser APIs. URL path
segments determine visibility: `[]` → All, `["active"]` → Active,
`["completed"]` → Completed.

## Client Tests

Pure unit tests in `client/test/` using gleeunit. No browser or DOM — tests
call `update` with a `Model` and `Msg`, then assert the returned `Model` and
inspect the `Effect` payload.

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
