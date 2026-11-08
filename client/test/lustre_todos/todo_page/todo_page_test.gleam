import gleam/list
import gleam/option.{None, Some}
import gleam/time/timestamp.{type Timestamp}
import gleeunit/should
import lustre_todos/shared/api_error
import lustre_todos/shared/effect.{HttpRequest, Message}
import lustre_todos/shared/http_effect.{Delete}
import lustre_todos/shared/out_msg
import lustre_todos/shared/remote.{Failed, Loaded, Loading}
import lustre_todos/todo_page/todo_item.{type Todo}
import lustre_todos/todo_page/todo_page
import youid/uuid.{type Uuid}

// ── Helpers ──────────────────────────────────────────────────────────────────

fn id1() -> Uuid {
  let assert Ok(id) = uuid.from_string("00000000-0000-0000-0000-000000000001")
  id
}

fn id2() -> Uuid {
  let assert Ok(id) = uuid.from_string("00000000-0000-0000-0000-000000000002")
  id
}

fn timestamp0() -> Timestamp {
  timestamp.from_unix_seconds(0)
}

fn todo1() -> Todo {
  todo_item.Todo(
    id: id1(),
    title: "Buy milk",
    completed: False,
    created_at: timestamp0(),
  )
}

fn todo2() -> Todo {
  todo_item.Todo(
    id: id2(),
    title: "Walk dog",
    completed: True,
    created_at: timestamp0(),
  )
}

fn id3() -> Uuid {
  let assert Ok(id) = uuid.from_string("00000000-0000-0000-0000-000000000003")
  id
}

fn todo3() -> Todo {
  todo_item.Todo(
    id: id3(),
    title: "Pay bills",
    completed: True,
    created_at: timestamp0(),
  )
}

fn todos_of(model: todo_page.Model) -> List(Todo) {
  remote.unwrap(model.todos, or: [])
}

fn empty_model() -> todo_page.Model {
  todo_page.Model(
    new_todo: "",
    todos: Loaded([]),
    visibility: todo_page.All,
    edit_state: None,
  )
}

fn model_with_todos() -> todo_page.Model {
  todo_page.Model(
    new_todo: "",
    todos: Loaded([todo1(), todo2()]),
    visibility: todo_page.All,
    edit_state: None,
  )
}

// Update: TodoActionFailed (non-401) rollback 
// INVARIANT: When an optimistic action fails, the model must be restored to
// its pre-action state. Without rollback, a failed toggle leaves the UI
// showing the opposite of what the server persisted.
pub fn update_todo_action_failed_non_401_rolls_back_test() {
  // Given a model where a todo was optimistically toggled
  let model =
    todo_page.Model(
      ..empty_model(),
      todos: Loaded([
        todo_item.Todo(..todo1(), completed: True),
        todo2(),
      ]),
    )

  // When that toggle fails with a non-401 error
  let #(new_model, _effect, _out_msgs) =
    todo_page.update(
      model,
      todo_page.TodoActionFailed(
        todo_page.UpdateCompleted(id1(), False),
        api_error.ApiError(
          error: "Server Error",
          details: "Something went wrong",
          status_code: Some(500),
          request_id: None,
        ),
      ),
    )

  // Then the optimistic change should be rolled back
  let assert Ok(item) =
    list.find(todos_of(new_model), fn(item) { item.id == id1() })
  item.completed |> should.equal(False)
}

// Update: UserSubmittedEditedTodo (empty title → delete)
// INVARIANT: Submitting an empty/whitespace title is treated as a delete.
// The handler must clear edit state, not touch the model, and dispatch a
// Message(UserDeletedTodo) — not fire HTTP itself — so deletion stays a
// single code path with its own rollback.
pub fn update_submit_edited_empty_title_deletes_test() {
  // Given a model in edit mode with a whitespace-only title
  let model =
    todo_page.Model(
      ..model_with_todos(),
      edit_state: Some(todo_page.EditState(id1(), "   ")),
    )

  // When the user submits the edit
  let #(new_model, effect, _out_msgs) =
    todo_page.update(model, todo_page.UserSubmittedEditedTodo)

  // Then the edit state should be cleared
  new_model.edit_state |> should.equal(None)
  // And the todo should still be present (deletion is scheduled via After)
  let assert Ok(_) =
    list.find(todos_of(new_model), fn(item) { item.id == id1() })
  // And an effect to delete the todo should be produced
  let assert Message(todo_page.UserDeletedTodo(id)) = effect
  id |> should.equal(id1())
}

// Update: UserDeletedTodo
// INVARIANT: Deleting a todo must remove it from the model and fire an HTTP
// DELETE. If the HTTP fails, TodoActionFailed(Delete(...)) rollback must
// re-insert it at its original position.
pub fn update_delete_todo_test() {
  // Given a model with two todos
  let model = model_with_todos()

  // When the user deletes one
  let #(new_model, effect, _out_msgs) =
    todo_page.update(model, todo_page.UserDeletedTodo(id2()))

  // Then the deleted todo should be removed
  todos_of(new_model) |> list.length |> should.equal(1)
  let assert [item] = todos_of(new_model)
  item.id |> should.equal(id1())
  // And a DELETE effect should be produced
  let assert HttpRequest(method: Delete, url:, ..) = effect
  url |> should.equal("/api/todos/00000000-0000-0000-0000-000000000002")
}

// Update: UserDeletedCompletedTodos
// INVARIANT: This handler should delete all the todos and send a batch delete
// request. This is more efficient than fanning out which comes at a cost of
// O(M * N) where M is the total number of todos and N is the fan-out (completed
// todos) due to needing to scan the todos list for each delete operation.
// The batch delete is O(M) since only one linear scan is needed.
pub fn update_delete_completed_todos_batches_test() {
  // Given a model with three todos, two of which are completed
  let model =
    todo_page.Model(..empty_model(), todos: Loaded([todo1(), todo2(), todo3()]))

  // When the user clears completed todos
  let #(new_model, effect, _out_msgs) =
    todo_page.update(model, todo_page.UserDeletedCompletedTodos)

  // Then all completed todos are deleted
  todos_of(new_model) |> list.length |> should.equal(1)
  let assert Ok(todo_item.Todo(completed: False, ..)) =
    list.first(todos_of(new_model))
  // And the effect should be a request for batch deletion
  let assert effect.HttpRequest(
    method: http_effect.Delete,
    url: "/api/todos/completed",
    ..,
  ) = effect
}

// Update: TodoActionFailed (Delete) rollback re-inserts in order
// INVARIANT: When a delete fails, the todo must be re-inserted at its
// original position (UUID-sorted), not appended. Appending would rearrange
// the list on every network blip, breaking the user's mental model of order.
pub fn update_todo_action_failed_delete_rolls_back_in_order_test() {
  // Given a model where todo2 was optimistically deleted (only todo1 and todo3 remain)
  let model =
    todo_page.Model(..empty_model(), todos: Loaded([todo1(), todo3()]))

  // When that delete fails with a non-401 error
  let #(new_model, _effect, _out_msgs) =
    todo_page.update(
      model,
      todo_page.TodoActionFailed(
        todo_page.Delete(todo2()),
        api_error.ApiError(
          error: "Server Error",
          details: "Something went wrong",
          status_code: Some(500),
          request_id: None,
        ),
      ),
    )

  // Then todo2 should be re-inserted in sorted order (by UUID string)
  todos_of(new_model) |> list.length |> should.equal(3)
  let assert [a, b, c] = todos_of(new_model)
  a.id |> should.equal(id1())
  b.id |> should.equal(id2())
  c.id |> should.equal(id3())
}

// Update: TodoActionFailed (401)
// INVARIANT: A 401 must roll back the optimistic change and surface an error
// toast like any other failure. The redirect to login is handled centrally in
// `app.gleam` (`wrap_http_requests`), so the page must not emit a redirect.
pub fn update_todo_action_failed_401_rolls_back_test() {
  // Given a model where a todo was optimistically toggled
  let model =
    todo_page.Model(
      ..empty_model(),
      todos: Loaded([
        todo_item.Todo(..todo1(), completed: True),
        todo2(),
      ]),
    )

  // When that toggle fails with a 401
  let #(new_model, effect, out_msgs) =
    todo_page.update(
      model,
      todo_page.TodoActionFailed(
        todo_page.UpdateCompleted(id1(), False),
        api_error.ApiError(
          error: "Unauthorized",
          details: "",
          status_code: Some(401),
          request_id: None,
        ),
      ),
    )

  // Then the optimistic change should be rolled back
  let assert Ok(item) =
    list.find(todos_of(new_model), fn(item) { item.id == id1() })
  item.completed |> should.equal(False)
  // And an error toast should be requested (no redirect to login)
  out_msgs |> list.length |> should.equal(1)
  let assert Ok(out_msg.PageRequestedToast(..)) = list.first(out_msgs)
  let assert effect.LogError(_) = effect
}

// Loading: the local copy, the server's list, and failures
// INVARIANT: The local copy shows only while the server has not answered, and
// never replaces the server's list. A failed fetch with nothing to show offers
// a retry; with the local copy showing, it only warns.
pub fn local_copy_shows_while_loading_test() {
  let model = todo_page.Model(..empty_model(), todos: Loading)

  let #(model, _, _) =
    todo_page.update(model, todo_page.ClientLoadedTodos([todo1()]))

  model.todos |> should.equal(Loaded([todo1()]))
}

pub fn local_copy_does_not_replace_the_servers_list_test() {
  let model = todo_page.Model(..empty_model(), todos: Loaded([todo2()]))

  let #(model, _, _) =
    todo_page.update(model, todo_page.ClientLoadedTodos([todo1()]))

  model.todos |> should.equal(Loaded([todo2()]))
}

pub fn failed_fetch_with_nothing_to_show_offers_a_retry_test() {
  let model = todo_page.Model(..empty_model(), todos: Loading)

  let #(model, _, out_msgs) =
    todo_page.update(model, todo_page.ClientFetchedTodos(Error(server_error())))

  model.todos |> should.equal(Failed)
  out_msgs |> should.equal([])

  let #(model, effect, _) = todo_page.update(model, todo_page.UserRetriedFetch)

  model.todos |> should.equal(Loading)
  let assert HttpRequest(method: http_effect.Get, url: "/api/todos", ..) =
    effect
}

pub fn failed_fetch_keeps_the_local_copy_test() {
  let model = todo_page.Model(..empty_model(), todos: Loaded([todo1()]))

  let #(model, _, out_msgs) =
    todo_page.update(model, todo_page.ClientFetchedTodos(Error(server_error())))

  model.todos |> should.equal(Loaded([todo1()]))
  out_msgs |> list.length |> should.equal(1)
}

pub fn nothing_is_saved_while_loading_test() {
  let model = todo_page.Model(..empty_model(), todos: Loading)

  let #(_, effect, _) =
    todo_page.update_with_storage(model, todo_page.UserChangedNewTodo("a"))

  let assert effect.Batch([_, effect.NoEffect]) = effect
}

fn server_error() -> api_error.ApiError {
  api_error.ApiError(
    error: "Server Error",
    details: "Something went wrong",
    status_code: Some(500),
    request_id: None,
  )
}
