import app
import gleeunit/should
import lustre_todos/shared/effect
import lustre_todos/shared/http_effect
import lustre_todos/todo_page/todo_page

// ── 401 interception ──────────────────────────────────────────────────────────
// `wrap_http_requests` rewrites every `HttpRequest` callback so a 401 response
// dispatches `SessionExpired` instead of reaching the page. These tests call
// the wrapped callback directly (pure, no browser), asserting both the
// interception and the pass-through of everything else.

pub fn intercepts_401_in_top_level_request_test() {
  let effect: effect.Effect(app.Msg) =
    effect.get("/api/todos", fn(_) { app.UrlChanged("") })
    |> app.wrap_http_requests

  let assert effect.HttpRequest(callback: callback, ..) = effect

  callback(Error(http_effect.HttpError(status: 401, body: "")))
  |> should.equal(app.SessionExpired)
}

pub fn intercepts_401_nested_inside_batch_test() {
  let effect: effect.Effect(app.Msg) =
    effect.batch([
      effect.get("/api/todos", fn(_) { app.UrlChanged("") }),
      effect.set_title("LustreTodos"),
    ])
    |> app.wrap_http_requests

  let assert effect.Batch([effect.HttpRequest(callback: callback, ..), ..]) =
    effect

  callback(Error(http_effect.HttpError(status: 401, body: "")))
  |> should.equal(app.SessionExpired)
}

pub fn passes_through_ok_responses_test() {
  let effect: effect.Effect(app.Msg) =
    effect.get("/api/todos", fn(_) { app.UrlChanged("") })
    |> app.wrap_http_requests

  let assert effect.HttpRequest(callback: callback, ..) = effect

  callback(Ok("[]"))
  |> should.equal(app.UrlChanged(""))
}

pub fn passes_through_non_401_errors_test() {
  let effect: effect.Effect(app.Msg) =
    effect.get("/api/todos", fn(_) { app.UrlChanged("") })
    |> app.wrap_http_requests

  let assert effect.HttpRequest(callback: callback, ..) = effect

  callback(Error(http_effect.HttpError(status: 500, body: "{}")))
  |> should.equal(app.UrlChanged(""))
}

pub fn passes_through_network_errors_test() {
  let effect: effect.Effect(app.Msg) =
    effect.get("/api/todos", fn(_) { app.UrlChanged("") })
    |> app.wrap_http_requests

  let assert effect.HttpRequest(callback: callback, ..) = effect

  callback(Error(http_effect.NetworkError("offline")))
  |> should.equal(app.UrlChanged(""))
}

// ── Routing ───────────────────────────────────────────────────────────────────

pub fn url_change_starts_the_todo_page_with_its_filter_test() {
  let #(model, _) = app.init(Nil)
  let #(model, _) = app.update(model, app.UrlChanged("/active"))

  let assert app.TodoPage(page) = model.page
  page.visibility |> should.equal(todo_page.Active)
}

pub fn url_change_on_the_todo_page_keeps_its_state_test() {
  let #(model, _) = app.init(Nil)
  let #(model, _) = app.update(model, app.UrlChanged("/"))
  let #(model, _) =
    app.update(model, app.TodoPageMsg(todo_page.UserChangedNewTodo("Draft")))
  let #(model, _) = app.update(model, app.UrlChanged("/completed"))

  let assert app.TodoPage(page) = model.page
  page.visibility |> should.equal(todo_page.Completed)
  page.new_todo |> should.equal("Draft")
}

pub fn unknown_url_shows_not_found_test() {
  let #(model, _) = app.init(Nil)
  let #(model, _) = app.update(model, app.UrlChanged("/"))
  let #(model, _) = app.update(model, app.UrlChanged("/no/such/page"))

  model.page |> should.equal(app.NotFound)
}

// ── Auth ──────────────────────────────────────────────────────────────────────

pub fn session_expiry_logs_in_returning_to_the_current_page_test() {
  let #(model, _) = app.init(Nil)
  let #(model, _) = app.update(model, app.UrlChanged("/active?x=1"))

  let #(_, effect) = app.update(model, app.SessionExpired)

  effect |> should.equal(effect.Redirect("/login?returnUrl=%2Factive%3Fx%3D1"))
}

pub fn signed_out_page_makes_no_request_test() {
  let #(model, _) = app.init(Nil)
  let #(model, effect) = app.update(model, app.UrlChanged("/signed-out"))

  model.page |> should.equal(app.SignedOut)
  let assert effect.Batch([effect.NoEffect, effect.SetTitle(_)]) = effect
}
