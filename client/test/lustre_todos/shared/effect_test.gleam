import gleam/option.{None, Some}
import gleeunit/should
import lustre_todos/shared/effect
import lustre_todos/shared/http_effect.{Put}

pub fn http_effects_default_to_no_timeout_test() {
  let effect: effect.Effect(Nil) = effect.get("/api/todos", fn(_) { Nil })

  let assert effect.HttpRequest(timeout: timeout, ..) = effect
  timeout |> should.equal(None)
}

pub fn with_timeout_arms_the_http_effect_test() {
  let effect: effect.Effect(Nil) =
    effect.post("/api/todos", "{}", fn(_) { Nil })
    |> effect.with_timeout(10_000)

  let assert effect.HttpRequest(timeout: timeout, ..) = effect
  timeout |> should.equal(Some(10_000))
}

pub fn with_timeout_preserves_http_fields_test() {
  let effect: effect.Effect(Nil) =
    effect.put("/api/todos/some-id", "{}", fn(_) { Nil })
    |> effect.with_timeout(5000)

  let assert effect.HttpRequest(
    method: method,
    url: url,
    body: body,
    content_type: content_type,
    timeout: timeout,
    ..,
  ) = effect

  method |> should.equal(Put)
  url |> should.equal("/api/todos/some-id")
  body |> should.equal("{}")
  content_type |> should.equal("application/json")
  timeout |> should.equal(Some(5000))
}

pub fn with_timeout_passes_other_effects_through_test() {
  let effect: effect.Effect(Nil) = effect.set_title("LustreTodos")

  let assert effect.SetTitle(title: title) = effect.with_timeout(effect, 10_000)
  title |> should.equal("LustreTodos")
}
