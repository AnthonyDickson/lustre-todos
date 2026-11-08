import app
import gleeunit/should
import lustre_todos/shared/effect
import lustre_todos/shared/http_effect

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
