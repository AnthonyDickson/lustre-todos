import gleam/bool
import gleam/dynamic/decode as dynamic_decode
import gleam/int
import gleam/json
import gleam/list
import gleam/option.{type Option, None, Some}
import gleam/string
import gleam/time/timestamp
import lustre/attribute
import lustre/element.{type Element}
import lustre/element/html
import lustre/event
import lustre_todos/shared/api_error
import lustre_todos/shared/api_route
import lustre_todos/shared/auth_route
import lustre_todos/shared/effect
import lustre_todos/shared/guard
import lustre_todos/shared/out_msg.{type OutMsg}
import lustre_todos/shared/remote.{type Remote, Failed, Loaded, Loading}
import lustre_todos/shared/response
import lustre_todos/shared/route
import lustre_todos/todo_page/todo_item
import youid/uuid

// ── Types ────────────────────────────────────────────────────────────────────

pub type Visibility {
  All
  Active
  Completed
}

pub type EditState {
  EditState(id: uuid.Uuid, new_title: String)
}

pub type TodoAction {
  UpdateCompleted(id: uuid.Uuid, previous_state: Bool)
  UpdateTitle(id: uuid.Uuid, previous_title: String)
  Create(id: uuid.Uuid)
  Delete(previous_todo: todo_item.Todo)
  DeleteCompleted(List(todo_item.Todo))
}

pub type Model {
  Model(
    new_todo: String,
    /// `Loaded` once the server or the local copy has supplied the list.
    todos: Remote(List(todo_item.Todo)),
    visibility: Visibility,
    edit_state: Option(EditState),
  )
}

pub type Msg {
  ClientLoadedTodos(todos: List(todo_item.Todo))
  ClientFetchedTodos(result: Result(List(todo_item.Todo), api_error.ApiError))
  UserRetriedFetch
  TodoActionFailed(action: TodoAction, error: api_error.ApiError)
  UserChangedNewTodo(text: String)
  UserSubmittedNewTodo
  UserToggledCompletedStatus(id: uuid.Uuid)
  UserEnteredEditMode(id: uuid.Uuid)
  UserEditedTodo(text: String)
  UserExitedEditMode
  UserSubmittedEditedTodo
  UserDeletedTodo(id: uuid.Uuid)
  UserDeletedCompletedTodos
  UserChangedVisibility(visibility: Visibility)
  NoOp
}

// ── Constants ────────────────────────────────────────────────────────────────

const local_storage_key = "todomvc-lustre"

/// How long a request may take before it fails, so a hung write is rolled back
/// rather than left pending.
const request_timeout_ms = 10_000

// ── Helpers ──────────────────────────────────────────────────────────────────

/// The list, or no todos while it is loading or failed to load. Actions find
/// nothing in an empty list, so they do nothing until the list is loaded.
fn loaded_todos(model: Model) -> List(todo_item.Todo) {
  remote.unwrap(model.todos, or: [])
}

/// Replace the loaded list. While nothing is loaded the change is dropped.
fn with_todos(model: Model, todos: List(todo_item.Todo)) -> Model {
  Model(..model, todos: remote.map(model.todos, fn(_) { todos }))
}

fn rollback(model: Model, action: TodoAction) -> Model {
  case action {
    UpdateCompleted(id, previous_state) ->
      with_todos(
        model,
        list.map(loaded_todos(model), fn(t) {
          case t.id == id {
            True -> todo_item.Todo(..t, completed: previous_state)
            False -> t
          }
        }),
      )
    UpdateTitle(id, previous_title) ->
      with_todos(
        model,
        list.map(loaded_todos(model), fn(t) {
          case t.id == id {
            True -> todo_item.Todo(..t, title: previous_title)
            False -> t
          }
        }),
      )
    Create(id) ->
      with_todos(model, list.filter(loaded_todos(model), fn(t) { t.id != id }))
    Delete(previous_todo) -> {
      let todos =
        list.append(loaded_todos(model), [previous_todo])
        |> list.sort(fn(a, b) {
          string.compare(uuid.to_string(a.id), uuid.to_string(b.id))
        })
      with_todos(model, todos)
    }
    DeleteCompleted(completed_todos) -> {
      let todos =
        list.append(loaded_todos(model), completed_todos)
        |> list.sort(fn(a, b) {
          string.compare(uuid.to_string(a.id), uuid.to_string(b.id))
        })
      with_todos(model, todos)
    }
  }
}

fn create_toast(model: Model, action: TodoAction) -> Option(out_msg.OutMsg) {
  case action {
    UpdateCompleted(id, previous_state) ->
      case list.find(loaded_todos(model), fn(t) { t.id == id }) {
        Ok(item) -> {
          let state_text = case previous_state {
            True -> "completed"
            False -> "not completed"
          }
          Some(out_msg.error_toast(
            "Could not sync changes",
            "Reverted the todo status from '"
              <> item.title
              <> "' to "
              <> state_text,
          ))
        }
        Error(Nil) -> None
      }
    UpdateTitle(id, previous_title) ->
      case list.find(loaded_todos(model), fn(t) { t.id == id }) {
        Ok(item) ->
          Some(out_msg.error_toast(
            "Could not sync changes",
            "Reverted the todo title from '"
              <> item.title
              <> "' to '"
              <> previous_title
              <> "'",
          ))
        Error(Nil) -> None
      }
    Create(id) ->
      case list.find(loaded_todos(model), fn(t) { t.id == id }) {
        Ok(item) ->
          Some(out_msg.error_toast(
            "Could not sync changes",
            "Reverted the creation of the todo '" <> item.title <> "'",
          ))
        Error(Nil) -> None
      }
    Delete(previous_todo) ->
      Some(out_msg.error_toast(
        "Could not sync changes",
        "Reverted the deletion of the todo '" <> previous_todo.title <> "'",
      ))
    DeleteCompleted(completed_todos) ->
      Some(out_msg.error_toast(
        "Could not sync changes",
        "Reverted the deletion of "
          <> int.to_string(completed_todos |> list.length)
          <> " todos",
      ))
  }
}

fn visible_todos(
  todos: List(todo_item.Todo),
  visibility: Visibility,
) -> List(todo_item.Todo) {
  case visibility {
    All -> todos
    Active -> list.filter(todos, fn(t) { !t.completed })
    Completed -> list.filter(todos, fn(t) { t.completed })
  }
}

fn save_todos(todos: List(todo_item.Todo)) -> effect.Effect(msg) {
  let json_str =
    todos
    |> list.map(todo_item.todo_to_json)
    |> json.preprocessed_array
    |> json.to_string
  effect.SaveToStore(local_storage_key, json_str)
}

fn fetch_todos() -> effect.Effect(Msg) {
  effect.get(api_route.to_string(api_route.GetAllTodos), fn(result) {
    case result {
      Ok(body) ->
        ClientFetchedTodos(response.decode_success(
          body,
          todo_item.todos_decoder(),
        ))
      Error(http_err) ->
        ClientFetchedTodos(Error(response.http_error_to_api_error(http_err)))
    }
  })
  |> effect.with_timeout(request_timeout_ms)
}

fn load_todos_from_store() -> effect.Effect(Msg) {
  effect.LoadFromStore(key: local_storage_key, callback: fn(store_result) {
    case store_result {
      Ok(value) ->
        case json.parse(value, using: todo_item.todos_decoder()) {
          Ok(todos) -> ClientLoadedTodos(todos)
          Error(_) -> NoOp
        }
      Error(_) -> NoOp
    }
  })
}

// ── Init ─────────────────────────────────────────────────────────────────────

pub fn init(visibility: Visibility) -> #(Model, effect.Effect(Msg)) {
  let model = Model(new_todo: "", todos: Loading, visibility:, edit_state: None)

  #(model, effect.batch([fetch_todos(), load_todos_from_store()]))
}

// ── Update ───────────────────────────────────────────────────────────────────

pub fn update(
  model: Model,
  msg: Msg,
) -> #(Model, effect.Effect(Msg), List(OutMsg)) {
  case msg {
    // The local copy shows while the server responds. It never replaces the
    // server's list.
    ClientLoadedTodos(todos) ->
      case model.todos {
        Loading -> #(Model(..model, todos: Loaded(todos)), effect.none(), [])
        Loaded(_) | Failed -> #(model, effect.none(), [])
      }

    ClientFetchedTodos(Ok(todos)) -> #(
      Model(..model, todos: Loaded(todos)),
      save_todos(todos),
      [],
    )

    ClientFetchedTodos(Error(error)) -> {
      let log = effect.LogError(api_error.describe(error))
      case model.todos {
        Loaded(_) -> #(model, log, [
          out_msg.error_toast("Could not sync todos", "Showing local data"),
        ])
        Loading | Failed -> #(Model(..model, todos: Failed), log, [])
      }
    }

    UserRetriedFetch -> #(Model(..model, todos: Loading), fetch_todos(), [])

    TodoActionFailed(action, error) -> {
      let updated_model = rollback(model, action)
      case create_toast(model, action) {
        Some(toast_msg) -> #(
          updated_model,
          effect.LogError(api_error.describe(error)),
          [toast_msg],
        )
        None -> #(updated_model, effect.LogError(api_error.describe(error)), [])
      }
    }

    UserChangedNewTodo(text) -> #(
      Model(..model, new_todo: text),
      effect.none(),
      [],
    )

    UserSubmittedNewTodo -> {
      let title = string.trim(model.new_todo)

      use <- bool.lazy_guard(when: string.is_empty(title), return: fn() {
        #(Model(..model, new_todo: ""), effect.none(), [])
      })

      let now = timestamp.system_time()
      let item =
        todo_item.Todo(id: uuid.v7(), title:, completed: False, created_at: now)
      let body =
        item
        |> todo_item.todo_to_json
        |> json.to_string

      let create_effect =
        effect.post(api_route.to_string(api_route.CreateTodo), body, fn(result) {
          case result {
            Ok(_) -> NoOp
            Error(err) ->
              TodoActionFailed(
                Create(item.id),
                response.http_error_to_api_error(err),
              )
          }
        })
        |> effect.with_timeout(request_timeout_ms)

      #(
        with_todos(
          Model(..model, new_todo: ""),
          list.append(loaded_todos(model), [item]),
        ),
        create_effect,
        [],
      )
    }

    UserToggledCompletedStatus(id) -> {
      use item <- guard.ok(
        in: list.find(loaded_todos(model), fn(t) { t.id == id }),
        else_return: #(Model(..model, edit_state: None), effect.none(), []),
      )

      let updated_item = todo_item.Todo(..item, completed: !item.completed)
      let todos =
        list.map(loaded_todos(model), fn(t) {
          case t.id == id {
            True -> updated_item
            False -> t
          }
        })
      let request =
        todo_item.UpdateTodoRequest(
          title: updated_item.title,
          completed: updated_item.completed,
        )
      let body =
        request
        |> todo_item.update_todo_request_to_json
        |> json.to_string
      let url = api_route.to_string(api_route.UpdateTodo(id))
      let patch_effect =
        effect.patch(url, body, fn(result) {
          case result {
            Ok(_) -> NoOp
            Error(err) ->
              TodoActionFailed(
                UpdateCompleted(updated_item.id, item.completed),
                response.http_error_to_api_error(err),
              )
          }
        })
        |> effect.with_timeout(request_timeout_ms)
      #(with_todos(model, todos), patch_effect, [])
    }

    UserEnteredEditMode(id) ->
      case list.find(loaded_todos(model), fn(t) { t.id == id }) {
        Ok(item) -> #(
          Model(
            ..model,
            edit_state: Some(EditState(id:, new_title: item.title)),
          ),
          effect.none(),
          [],
        )
        Error(Nil) -> #(model, effect.none(), [])
      }

    UserEditedTodo(text) -> {
      let edit_state =
        option.map(model.edit_state, fn(e) { EditState(..e, new_title: text) })
      #(Model(..model, edit_state:), effect.none(), [])
    }

    UserExitedEditMode -> #(Model(..model, edit_state: None), effect.none(), [])

    UserSubmittedEditedTodo -> {
      use EditState(id:, new_title:) <- guard.some(
        in: model.edit_state,
        else_return: #(Model(..model, edit_state: None), effect.none(), []),
      )
      let new_title = string.trim(new_title)

      use <- bool.lazy_guard(when: string.is_empty(new_title), return: fn() {
        #(
          Model(..model, edit_state: None),
          effect.Message(UserDeletedTodo(id)),
          [],
        )
      })

      use item <- guard.ok(
        in: list.find(loaded_todos(model), fn(t) { t.id == id }),
        else_return: #(Model(..model, edit_state: None), effect.none(), []),
      )

      let todos =
        list.map(loaded_todos(model), fn(t) {
          case t.id == id {
            True -> todo_item.Todo(..t, title: new_title)
            False -> t
          }
        })
      let request =
        todo_item.UpdateTodoRequest(title: new_title, completed: item.completed)
      let body =
        request
        |> todo_item.update_todo_request_to_json
        |> json.to_string
      let url = api_route.to_string(api_route.UpdateTodo(id))
      let patch_effect =
        effect.patch(url, body, fn(result) {
          case result {
            Ok(_) -> NoOp
            Error(err) ->
              TodoActionFailed(
                UpdateTitle(item.id, item.title),
                response.http_error_to_api_error(err),
              )
          }
        })
        |> effect.with_timeout(request_timeout_ms)
      #(with_todos(Model(..model, edit_state: None), todos), patch_effect, [])
    }

    UserDeletedTodo(id) -> {
      use item_to_remove <- guard.ok(
        in: list.find(loaded_todos(model), fn(t) { t.id == id }),
        else_return: #(model, effect.none(), []),
      )

      let todos = list.filter(loaded_todos(model), fn(t) { t.id != id })
      let url = api_route.to_string(api_route.DeleteTodo(id))
      let delete_effect =
        effect.delete(url, fn(result) {
          case result {
            Ok(_) -> NoOp
            Error(err) ->
              TodoActionFailed(
                Delete(item_to_remove),
                response.http_error_to_api_error(err),
              )
          }
        })
        |> effect.with_timeout(request_timeout_ms)

      #(with_todos(model, todos), delete_effect, [])
    }

    UserDeletedCompletedTodos -> {
      let #(completed, active) =
        list.partition(loaded_todos(model), fn(t) { t.completed })
      let delete_effect =
        effect.delete(
          api_route.to_string(api_route.DeleteCompletedTodos),
          fn(result) {
            case result {
              Ok(_) -> NoOp
              Error(err) ->
                TodoActionFailed(
                  DeleteCompleted(completed),
                  response.http_error_to_api_error(err),
                )
            }
          },
        )
        |> effect.with_timeout(request_timeout_ms)
      #(with_todos(model, active), delete_effect, [])
    }

    UserChangedVisibility(visibility) -> #(
      Model(..model, visibility:),
      effect.none(),
      [],
    )

    NoOp -> #(model, effect.none(), [])
  }
}

// ── Update with storage ──────────────────────────────────────────────────────

pub fn update_with_storage(
  model: Model,
  msg: Msg,
) -> #(Model, effect.Effect(Msg), List(OutMsg)) {
  let #(new_model, effects, out_msgs) = update(model, msg)
  // Saving while nothing is loaded would replace the local copy with nothing.
  let save = case new_model.todos {
    Loaded(todos) -> save_todos(todos)
    Loading | Failed -> effect.none()
  }
  #(new_model, effect.batch([effects, save]), out_msgs)
}

// ── View ─────────────────────────────────────────────────────────────────────

fn todo_list_item(
  edit_state: Option(EditState),
  item: todo_item.Todo,
) -> Element(Msg) {
  let li_classes =
    "bg-gray-50 py-5 min-w-xl text-2xl border-t-1 border-gray-200 flex items-center group"

  case edit_state {
    Some(EditState(id:, new_title:)) if id == item.id ->
      html.li([attribute.class(li_classes)], [
        html.input([
          attribute.type_("text"),
          attribute.autofocus(True),
          attribute.value(new_title),
          attribute.attribute("data-testid", "edit-todo-input"),
          event.on_input(fn(text) { UserEditedTodo(text) }),
          event.on_keydown(fn(key) {
            case key {
              "Enter" -> UserSubmittedEditedTodo
              _ -> NoOp
            }
          }),
          event.on_blur(UserExitedEditMode),
          attribute.placeholder("What needs to be done?"),
          attribute.class(
            "text-gray-600 text-2xl bg-gray-50 focus-visible:outline-none px-15 min-w-xl placeholder:text-2xl placeholder:text-gray-300 placeholder:italic",
          ),
        ]),
      ])
    _ ->
      html.li(
        [
          attribute.class(li_classes),
          attribute.attribute("data-testid", "todo-item"),
          event.on(
            "dblclick",
            dynamic_decode.success(UserEnteredEditMode(item.id)),
          ),
        ],
        [
          html.input([
            attribute.type_("checkbox"),
            attribute.class("w-5 mx-5"),
            attribute.checked(item.completed),
            attribute.attribute("data-testid", "todo-checkbox"),
            event.on_check(fn(_) { UserToggledCompletedStatus(item.id) }),
          ]),
          html.p(
            [
              attribute.attribute("data-testid", "todo-title"),
              attribute.class(case item.completed {
                True -> "line-through text-gray-300"
                False -> "text-gray-600"
              }),
            ],
            [element.text(item.title)],
          ),
          html.button(
            [
              attribute.class(
                "ml-auto mx-5 w-5 text-red-400/0 group-hover:text-red-400",
              ),
              attribute.attribute("data-testid", "delete-todo"),
              event.on_click(UserDeletedTodo(item.id)),
            ],
            [element.text("x")],
          ),
        ],
      )
  }
}

fn visibility_classes(
  visibility: Visibility,
  model_visibility: Visibility,
) -> String {
  let base = "p-1 rounded-sm border-1"
  case visibility == model_visibility {
    True -> "border-rose-300/40 " <> base
    False -> "border-rose-300/0 hover:border-rose-300/20 " <> base
  }
}

pub fn view(model: Model) -> Element(Msg) {
  let #(active_count, completed_count) =
    list.fold(loaded_todos(model), #(0, 0), fn(acc, t) {
      let #(active, completed) = acc
      case t.completed {
        True -> #(active, completed + 1)
        False -> #(active + 1, completed)
      }
    })

  let todo_count = active_count + completed_count

  let visible_todos = visible_todos(loaded_todos(model), model.visibility)

  html.div([attribute.class("bg-gray-100 h-dvh flex h-screen justify-center")], [
    html.main([], [
      html.header([], [
        html.h1(
          [
            attribute.class("text-8xl text-rose-300/30 text-center m-5"),
          ],
          [element.text("todos")],
        ),
        html.input([
          attribute.type_("text"),
          attribute.autofocus(True),
          attribute.value(model.new_todo),
          attribute.placeholder("What needs to be done?"),
          attribute.attribute("data-testid", "new-todo-input"),
          attribute.disabled(case model.todos {
            Loaded(_) -> False
            Loading | Failed -> True
          }),
          attribute.class(
            "text-gray-600 text-2xl bg-gray-50 drop-shadow-sm focus-visible:outline-none py-5 px-15 min-w-xl placeholder:text-2xl placeholder:text-gray-300 placeholder:italic",
          ),
          event.on_input(fn(text) { UserChangedNewTodo(text) }),
          event.on_keydown(fn(key) {
            case key {
              "Enter" -> UserSubmittedNewTodo
              _ -> NoOp
            }
          }),
        ]),
      ]),
      case model.todos {
        Loaded(_) ->
          html.ol(
            [attribute.class("drop-shadow-sm")],
            list.map(visible_todos, todo_list_item(model.edit_state, _)),
          )
        Loading ->
          html.p(
            [
              attribute.class("text-gray-400 text-center py-5"),
              attribute.attribute("data-testid", "todos-loading"),
            ],
            [element.text("Loading…")],
          )
        Failed ->
          html.div(
            [
              attribute.class(
                "text-gray-500 flex flex-col items-center gap-2 py-5",
              ),
              attribute.attribute("data-testid", "todos-failed"),
            ],
            [
              html.p([], [element.text("Could not load your todos.")]),
              html.button(
                [
                  attribute.class("text-rose-400 hover:underline"),
                  attribute.attribute("data-testid", "retry-fetch"),
                  event.on_click(UserRetriedFetch),
                ],
                [element.text("Try again")],
              ),
            ],
          )
      },
      case todo_count > 0 {
        True ->
          html.footer(
            [
              attribute.class(
                "text-gray-500 text-sm bg-gray-50 drop-shadow-sm py-2 px-5 min-w-lg border-t-1 border-gray-200 flex justify-between",
              ),
            ],
            [
              html.p(
                [
                  attribute.class("pt-1"),
                  attribute.attribute("data-testid", "todo-count"),
                ],
                [
                  html.strong([], [element.text(int.to_string(active_count))]),
                  element.text(case active_count {
                    1 -> " item left"
                    _ -> " items left"
                  }),
                ],
              ),
              html.div([attribute.class("flex gap-2")], [
                html.a(
                  [
                    attribute.href(route.to_string(route.AllTodos)),
                    attribute.class(visibility_classes(All, model.visibility)),
                    attribute.attribute("data-testid", "filter-all"),
                  ],
                  [element.text("All")],
                ),
                html.a(
                  [
                    attribute.href(route.to_string(route.ActiveTodos)),
                    attribute.class(visibility_classes(Active, model.visibility)),
                    attribute.attribute("data-testid", "filter-active"),
                  ],
                  [element.text("Active")],
                ),
                html.a(
                  [
                    attribute.href(route.to_string(route.CompletedTodos)),
                    attribute.class(visibility_classes(
                      Completed,
                      model.visibility,
                    )),
                    attribute.attribute("data-testid", "filter-completed"),
                  ],
                  [element.text("Completed")],
                ),
              ]),
              html.button(
                [
                  attribute.class(
                    "hover:underline"
                    <> {
                      case completed_count {
                        0 -> " invisible"
                        _ -> ""
                      }
                    },
                  ),
                  attribute.attribute("data-testid", "clear-completed"),
                  event.on_click(UserDeletedCompletedTodos),
                ],
                [
                  element.text(
                    "Clear completed (" <> int.to_string(completed_count) <> ")",
                  ),
                ],
              ),
            ],
          )
        False -> element.none()
      },
      html.footer([attribute.class("flex justify-end py-2 px-5 min-w-lg")], [
        // Logging out is a form post, so another site cannot log the user out
        // with a link.
        html.form(
          [attribute.method("post"), attribute.action(auth_route.logout)],
          [
            html.button(
              [
                attribute.type_("submit"),
                attribute.class("text-sm text-gray-400 hover:text-gray-600"),
                attribute.attribute("data-testid", "logout"),
              ],
              [element.text("Logout")],
            ),
          ],
        ),
      ]),
    ]),
  ])
}
