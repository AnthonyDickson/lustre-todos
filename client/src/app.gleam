import gleam/list
import gleam/option.{None, Some}
import gleam/uri
import lustre
import lustre/effect as lustre_effect
import lustre/element.{type Element}
import lustre/element/html
import lustre_todos/shared/effect
import lustre_todos/shared/http_effect
import lustre_todos/shared/out_msg.{type OutMsg}
import lustre_todos/shared/toast.{type Toast}
import lustre_todos/todo_page/todo_page
import youid/uuid.{type Uuid}

pub type Model {
  Model(todo_page: todo_page.Model, toasts: List(Toast))
}

pub type Msg {
  SessionExpired
  UrlChanged(path: String)
  TodoPageMsg(todo_page.Msg)
  ToastDismissed(id: Uuid)
}

fn path_to_visibility(path: String) -> todo_page.Visibility {
  case uri.path_segments(path) {
    [] -> todo_page.All
    ["active"] -> todo_page.Active
    ["completed"] -> todo_page.Completed
    _ -> todo_page.All
  }
}

pub fn init(_flags) -> #(Model, effect.Effect(Msg)) {
  let #(todo_model, todo_effect) = todo_page.init()
  let model = Model(todo_page: todo_model, toasts: [])

  let effects =
    effect.batch([
      effect.map(todo_effect, TodoPageMsg),
      effect.init_routing(UrlChanged),
      effect.set_title("LustreTodos"),
    ])

  #(model, effects)
}

fn map_out_msg(
  out_msg: OutMsg,
  model: Model,
  effect: effect.Effect(Msg),
) -> #(Model, effect.Effect(Msg)) {
  case out_msg {
    out_msg.PageRequestedToast(title:, body:, level:, dismiss_after_ms:) -> {
      let new_toast = toast.Toast(id: uuid.v7(), title:, body:, level:)
      let model = Model(..model, toasts: [new_toast, ..model.toasts])
      let effect = case dismiss_after_ms {
        Some(delay) ->
          effect.batch([
            effect,
            effect.After(delay, ToastDismissed(new_toast.id)),
          ])
        None -> effect
      }
      #(model, effect)
    }
  }
}

fn with_out_msgs(
  update_output: #(Model, effect.Effect(Msg)),
  out_msgs: List(OutMsg),
) -> #(Model, effect.Effect(Msg)) {
  case out_msgs {
    [] -> update_output
    [msg, ..other_msgs] -> {
      let #(model, effect) = update_output
      map_out_msg(msg, model, effect)
      |> with_out_msgs(other_msgs)
    }
  }
}

pub fn update(model: Model, msg: Msg) -> #(Model, effect.Effect(Msg)) {
  case msg {
    UrlChanged(path) -> {
      let visibility = path_to_visibility(path)
      let #(todo_model, todo_effect, out_msgs) =
        todo_page.update_with_storage(
          model.todo_page,
          todo_page.UserChangedVisibility(visibility),
        )
      #(
        Model(..model, todo_page: todo_model),
        effect.map(todo_effect, TodoPageMsg),
      )
      |> with_out_msgs(out_msgs)
    }
    TodoPageMsg(inner_msg) -> {
      let #(inner_model, inner_effect, out_msgs) =
        todo_page.update_with_storage(model.todo_page, inner_msg)
      #(
        Model(..model, todo_page: inner_model),
        effect.map(inner_effect, TodoPageMsg),
      )
      |> with_out_msgs(out_msgs)
    }
    ToastDismissed(id:) -> {
      let toasts = list.filter(model.toasts, fn(toast) { toast.id != id })
      #(Model(..model, toasts:), effect.none())
    }
    SessionExpired -> #(model, effect.Redirect("/login"))
  }
}

/// Rewrite every `HttpRequest` effect so a 401 response dispatches
/// `SessionExpired` instead of reaching the page's callback. Recurses through
/// `Batch` because effects reach this point wrapped by `with_auth_redirect`.
pub fn wrap_http_requests(effect: effect.Effect(Msg)) -> effect.Effect(Msg) {
  case effect {
    effect.HttpRequest(callback: original_callback, ..) as request ->
      effect.HttpRequest(..request, callback: fn(result) {
        case result {
          Error(http_effect.HttpError(status: 401, ..)) -> SessionExpired
          _ -> original_callback(result)
        }
      })
    effect.Batch(effects) -> effect.Batch(list.map(effects, wrap_http_requests))
    _ -> effect
  }
}

fn with_auth_redirect(
  result: #(Model, effect.Effect(Msg)),
) -> #(Model, effect.Effect(Msg)) {
  let #(model, effect) = result
  #(model, wrap_http_requests(effect))
}

pub fn view(model: Model) -> Element(Msg) {
  let page =
    todo_page.view(model.todo_page)
    |> element.map(TodoPageMsg)

  let toasts = toast.view_with_container(model.toasts, ToastDismissed)

  html.div([], [
    page,
    toasts,
  ])
}

fn update_with_effect(
  model: Model,
  msg: Msg,
) -> #(Model, lustre_effect.Effect(Msg)) {
  let #(new_model, custom_effect) =
    update(model, msg)
    |> with_auth_redirect
  #(
    new_model,
    lustre_effect.from(fn(dispatch) { effect.run(custom_effect, dispatch) }),
  )
}

pub fn main() {
  let #(init_model, init_effect) = init(Nil) |> with_auth_redirect

  let app =
    lustre.application(
      init: fn(_) {
        #(
          init_model,
          lustre_effect.from(fn(dispatch) { effect.run(init_effect, dispatch) }),
        )
      },
      update: update_with_effect,
      view: view,
    )

  let assert Ok(_) = lustre.start(app, "#app", Nil)
  Nil
}
