import gleam/dynamic/decode
import gleam/json
import gleam/list
import gleam/option
import gleam/string
import lustre_todos/shared/api_error
import lustre_todos/shared/http_effect.{type HttpError, HttpError, NetworkError}

/// Render a list of field-level decode errors into a human-readable string.
fn format_decode_errors(errors: List(decode.DecodeError)) -> String {
  errors
  |> list.map(fn(err) {
    let path = string.join(err.path, ".")
    "Expected " <> err.expected <> " at " <> path <> ", found " <> err.found
  })
  |> string.join("; ")
}

/// Render a top-level JSON parse error into a human-readable string.
fn format_decode_error(err: json.DecodeError) -> String {
  case err {
    json.UnexpectedEndOfInput -> "Unexpected end of JSON input"
    json.UnexpectedByte(byte) -> "Unexpected byte: " <> byte
    json.UnexpectedSequence(seq) -> "Unexpected sequence: " <> seq
    json.UnableToDecode(errors) -> format_decode_errors(errors)
  }
}

/// Decode a 2xx response body into a typed `Result(a, ApiError)`.
///
pub fn decode_success(
  body: String,
  decoder: decode.Decoder(a),
) -> Result(a, api_error.ApiError) {
  case json.parse(body, using: decoder) {
    Ok(value) -> Ok(value)
    Error(err) ->
      Error(api_error.ApiError(
        error: "Decode error",
        details: format_decode_error(err),
        status_code: option.None,
        request_id: option.None,
      ))
  }
}

/// Convert an `HttpError` into an `ApiError`. The error body is parsed as
/// `ApiError` JSON on a best-effort basis — if parsing fails a generic
/// `ApiError` is returned. `NetworkError` is surfaced as an `ApiError`
/// with no status code.
///
pub fn http_error_to_api_error(err: HttpError) -> api_error.ApiError {
  case err {
    HttpError(status, body) ->
      case json.parse(body, using: api_error.decoder()) {
        Ok(err) -> err
        Error(err) ->
          api_error.ApiError(
            error: "Unexpected response",
            details: format_decode_error(err),
            status_code: option.Some(status),
            request_id: option.None,
          )
      }
    NetworkError(msg) ->
      api_error.ApiError(
        error: "Network error",
        details: msg,
        status_code: option.None,
        request_id: option.None,
      )
  }
}
