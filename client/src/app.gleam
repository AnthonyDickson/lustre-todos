import gleam/list
import gleam/option.{None, Some}
import lustre
import lustre/attribute
import lustre/effect as lustre_effect
import lustre/element.{type Element}
import lustre/element/html
import lustre_todos/shared/auth_route
import lustre_todos/shared/effect
import lustre_todos/shared/http_effect
import lustre_todos/shared/out_msg.{type OutMsg}
import lustre_todos/shared/route
import lustre_todos/shared/toast.{type Toast}
import lustre_todos/todo_page/todo_page
import youid/uuid.{type Uuid}

/// The page the current route shows, with its state.
pub type Page {
  TodoPage(todo_page.Model)
  SignedOut
  NotFound
}

pub type Model {
  Model(
    page: Page,
    toasts: List(Toast),
    /// The current path, query, and fragment, so a login after the session
    /// expires returns to them.
    url: String,
  )
}

pub type Msg {
  SessionExpired
  UrlChanged(path: String)
  TodoPageMsg(todo_page.Msg)
  ToastDismissed(id: Uuid)
}

pub fn init(_flags) -> #(Model, effect.Effect(Msg)) {
  #(
    Model(page: NotFound, toasts: [], url: "/"),
    effect.init_routing(UrlChanged),
  )
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

fn update_todo_page(
  model: Model,
  page_model: todo_page.Model,
  msg: todo_page.Msg,
) -> #(Model, effect.Effect(Msg)) {
  let #(page_model, page_effect, out_msgs) =
    todo_page.update_with_storage(page_model, msg)
  #(
    Model(..model, page: TodoPage(page_model)),
    effect.map(page_effect, TodoPageMsg),
  )
  |> with_out_msgs(out_msgs)
}

/// Show the page for `route`.
fn change_page(
  model: Model,
  route: route.Route,
) -> #(Model, effect.Effect(Msg)) {
  case route {
    route.AllTodos -> show_todos(model, todo_page.All)
    route.ActiveTodos -> show_todos(model, todo_page.Active)
    route.CompletedTodos -> show_todos(model, todo_page.Completed)
    route.SignedOut -> #(Model(..model, page: SignedOut), effect.none())
    route.NotFound -> #(Model(..model, page: NotFound), effect.none())
  }
}

/// Staying on the todo page keeps its state and only changes the filter;
/// arriving from another page starts it afresh.
fn show_todos(
  model: Model,
  visibility: todo_page.Visibility,
) -> #(Model, effect.Effect(Msg)) {
  case model.page {
    TodoPage(page_model) ->
      update_todo_page(
        model,
        page_model,
        todo_page.UserChangedVisibility(visibility),
      )
    SignedOut | NotFound -> {
      let #(page_model, page_effect) = todo_page.init(visibility)
      #(
        Model(..model, page: TodoPage(page_model)),
        effect.map(page_effect, TodoPageMsg),
      )
    }
  }
}

pub fn update(model: Model, msg: Msg) -> #(Model, effect.Effect(Msg)) {
  case msg {
    UrlChanged(path) -> {
      let route = route.from_string(path)
      let #(model, page_effect) = change_page(Model(..model, url: path), route)
      #(
        model,
        effect.batch([page_effect, effect.set_title(route.title(route))]),
      )
    }
    TodoPageMsg(inner_msg) ->
      case model.page {
        TodoPage(page_model) -> update_todo_page(model, page_model, inner_msg)
        // A late response for a page the user has left
        SignedOut | NotFound -> #(model, effect.none())
      }
    ToastDismissed(id:) -> {
      let toasts = list.filter(model.toasts, fn(toast) { toast.id != id })
      #(Model(..model, toasts:), effect.none())
    }
    SessionExpired -> #(
      model,
      effect.Redirect(auth_route.login_returning_to(model.url)),
    )
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

fn signed_out_view() -> Element(Msg) {
  html.main(
    [
      attribute.class(
        "bg-gray-100 h-dvh flex flex-col items-center gap-4 pt-20",
      ),
      attribute.attribute("data-testid", "signed-out"),
    ],
    [
      html.h1([attribute.class("text-4xl text-gray-500")], [
        element.text("You have signed out"),
      ]),
      // A form, not a link: the client's router would catch a link to /login.
      html.form([attribute.method("get"), attribute.action(auth_route.login)], [
        html.button(
          [
            attribute.type_("submit"),
            attribute.class("text-rose-400 hover:underline"),
            attribute.attribute("data-testid", "sign-in"),
          ],
          [element.text("Sign in")],
        ),
      ]),
    ],
  )
}

fn not_found_view() -> Element(Msg) {
  html.main(
    [
      attribute.class(
        "bg-gray-100 h-dvh flex flex-col items-center gap-4 pt-20",
      ),
      attribute.attribute("data-testid", "not-found"),
    ],
    [
      html.h1([attribute.class("text-4xl text-gray-500")], [
        element.text("Page not found"),
      ]),
      html.a(
        [
          attribute.href(route.to_string(route.AllTodos)),
          attribute.class("text-rose-400 hover:underline"),
        ],
        [element.text("Back to your todos")],
      ),
    ],
  )
}

pub fn view(model: Model) -> Element(Msg) {
  let page = case model.page {
    TodoPage(page_model) ->
      todo_page.view(page_model) |> element.map(TodoPageMsg)
    SignedOut -> signed_out_view()
    NotFound -> not_found_view()
  }

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
