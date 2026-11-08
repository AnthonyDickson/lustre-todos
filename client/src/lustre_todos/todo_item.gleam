import gleam/dynamic/decode
import gleam/json
import gleam/time/calendar
import gleam/time/timestamp.{type Timestamp}
import youid/uuid.{type Uuid}

pub type Todo {
  Todo(id: Uuid, title: String, completed: Bool, created_at: Timestamp)
}

pub type UpdateTodoRequest {
  UpdateTodoRequest(title: String, completed: Bool)
}

fn uuid_decoder() -> decode.Decoder(Uuid) {
  decode.string
  |> decode.then(fn(s) {
    case uuid.from_string(s) {
      Ok(u) -> decode.success(u)
      Error(Nil) -> decode.failure(uuid.nil, "Uuid")
    }
  })
}

/// Instants are RFC 3339 strings, e.g. "2026-10-03T04:23:50.5Z". See docs/dates.md.
fn timestamp_decoder() -> decode.Decoder(Timestamp) {
  decode.string
  |> decode.then(fn(s) {
    case timestamp.parse_rfc3339(s) {
      Ok(t) -> decode.success(t)
      Error(Nil) -> decode.failure(timestamp.unix_epoch, "Timestamp")
    }
  })
}

pub fn todo_decoder() -> decode.Decoder(Todo) {
  use id <- decode.field("id", uuid_decoder())
  use title <- decode.field("title", decode.string)
  use completed <- decode.field("completed", decode.bool)
  use created_at <- decode.field("createdAt", timestamp_decoder())
  decode.success(Todo(id:, title:, completed:, created_at:))
}

pub fn todos_decoder() -> decode.Decoder(List(Todo)) {
  decode.list(todo_decoder())
}

pub fn todo_to_json(item: Todo) -> json.Json {
  json.object([
    #("id", json.string(uuid.to_string(item.id))),
    #("title", json.string(item.title)),
    #("completed", json.bool(item.completed)),
    #(
      "createdAt",
      json.string(timestamp.to_rfc3339(item.created_at, calendar.utc_offset)),
    ),
  ])
}

pub fn update_todo_request_to_json(req: UpdateTodoRequest) -> json.Json {
  json.object([
    #("title", json.string(req.title)),
    #("completed", json.bool(req.completed)),
  ])
}
