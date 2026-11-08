import gleam/fetch
import gleam/http
import gleam/http/request
import gleam/javascript/promise
import gleam/option.{type Option, None, Some}
import gleam/result

@external(javascript, "./effect_ffi.mjs", "getOrigin")
fn get_origin() -> String

@external(javascript, "./effect_ffi.mjs", "withTimeoutSignal")
fn with_timeout_signal(
  request: fetch.FetchRequest,
  timeout: Int,
) -> fetch.FetchRequest

/// The HTTP methods supported by this application.
pub type HttpMethod {
  Get
  Post
  Put
  Patch
  Delete
}

/// Error classification for HTTP effects. Separates transport failures from
/// HTTP-level errors so callers can branch on status codes without parsing
/// JSON (e.g. `Error(HttpError(401, _))` → redirect to login).
///
pub type HttpError {
  NetworkError(String)
  HttpError(status: Int, body: String)
}

fn to_http_method(method: HttpMethod) -> http.Method {
  case method {
    Get -> http.Get
    Post -> http.Post
    Put -> http.Put
    Patch -> http.Patch
    Delete -> http.Delete
  }
}

fn fetch_error_to_http_error(err: fetch.FetchError) -> HttpError {
  NetworkError(case err {
    fetch.NetworkError(msg) -> "Network error: " <> msg
    fetch.UnableToReadBody -> "Unable to read response body"
    fetch.InvalidJsonBody -> "Invalid JSON body in response"
  })
}

fn build_request(
  method: HttpMethod,
  url: String,
  body: String,
  content_type: String,
) -> request.Request(String) {
  let req =
    request.to(get_origin() <> url)
    |> result.unwrap(request.new())
    |> request.set_method(to_http_method(method))

  case body {
    "" -> req
    _ ->
      req
      |> request.set_body(body)
      |> request.set_header("content-type", content_type)
  }
}

/// Send an HTTP request and return the raw response. 2xx → `Ok(body)`,
/// non-2xx → `Error(HttpError(status, body))`, network failure →
/// `Error(NetworkError(description))`. Use `transform` to inject auth headers,
/// CSRF tokens, or other per-request customisation. A `timeout` of
/// `Some(ms)` aborts the request after `ms` milliseconds, surfacing as
/// `Error(NetworkError(..))`; `None` leaves the request unbounded.
///
pub fn send(
  method: HttpMethod,
  url: String,
  body: String,
  content_type: String,
  transform: fn(request.Request(String)) -> request.Request(String),
  timeout: Option(Int),
) -> promise.Promise(Result(String, HttpError)) {
  let req =
    build_request(method, url, body, content_type)
    |> transform

  let response = case timeout {
    None -> fetch.send(req)
    Some(ms) ->
      req
      |> fetch.to_fetch_request
      |> with_timeout_signal(ms)
      |> fetch.raw_send
      |> promise.try_await(fn(resp) {
        promise.resolve(Ok(fetch.from_fetch_response(resp)))
      })
  }

  response
  |> promise.map(result.map_error(_, fetch_error_to_http_error))
  |> promise.try_await(fn(resp) {
    fetch.read_text_body(resp)
    |> promise.map(result.map_error(_, fetch_error_to_http_error))
    |> promise.try_await(fn(text_resp) {
      case text_resp.status >= 200 && text_resp.status < 300 {
        True -> promise.resolve(Ok(text_resp.body))
        False ->
          promise.resolve(Error(HttpError(text_resp.status, text_resp.body)))
      }
    })
  })
}
