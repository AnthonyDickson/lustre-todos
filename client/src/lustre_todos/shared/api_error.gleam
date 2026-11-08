import gleam/dynamic/decode
import gleam/int
import gleam/option.{type Option, None, Some}

pub type ApiError {
  ApiError(
    error: String,
    details: String,
    status_code: Option(Int),
    request_id: Option(String),
  )
}

pub fn describe(error: ApiError) -> String {
  let ApiError(error, details, status_code, _request_id) = error
  let text = error <> ": " <> details

  case status_code {
    Some(status_code) -> int.to_string(status_code) <> " " <> text
    None -> text
  }
}

pub fn decoder() -> decode.Decoder(ApiError) {
  use error <- decode.field("error", decode.string)
  use details <- decode.field("details", decode.string)
  use status_code <- decode.field("statusCode", decode.optional(decode.int))
  use request_id <- decode.field("requestId", decode.optional(decode.string))
  decode.success(ApiError(error:, details:, status_code:, request_id:))
}
