//// The server's API endpoints, so each path is written once.

import youid/uuid.{type Uuid}

pub type ApiRoute {
  GetAllTodos
  CreateTodo
  UpdateTodo(id: Uuid)
  DeleteTodo(id: Uuid)
  DeleteCompletedTodos
}

const api_prefix = "/api"

pub fn to_string(route: ApiRoute) -> String {
  case route {
    GetAllTodos | CreateTodo -> api_prefix <> "/todos"
    UpdateTodo(id:) | DeleteTodo(id:) ->
      api_prefix <> "/todos/" <> uuid.to_string(id)
    DeleteCompletedTodos -> api_prefix <> "/todos/completed"
  }
}
