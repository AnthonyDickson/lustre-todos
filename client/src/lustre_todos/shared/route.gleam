import gleam/string
import gleam/uri

/// A route to a page in this SPA.
pub type Route {
  AllTodos
  ActiveTodos
  CompletedTodos
  NotFound
}

/// Convert a route into a path string.
pub fn to_string(route: Route) -> String {
  case route {
    AllTodos -> "/"
    ActiveTodos -> "/active"
    CompletedTodos -> "/completed"
    NotFound -> "/not-found"
  }
}

/// The browser title for a route: the page's name, then the app's.
pub fn title(route: Route) -> String {
  let name = case route {
    AllTodos | ActiveTodos | CompletedTodos -> "Todos"
    NotFound -> "Page Not Found"
  }
  name <> " | LustreTodos"
}

/// Get the route from a URL's path, ignoring any query or fragment. Defaults to
/// `NotFound` for an unrecognised path.
pub fn from_string(url: String) -> Route {
  let path = case uri.parse(url) {
    Ok(parsed) -> parsed.path
    Error(Nil) -> url
  }

  case path |> string.lowercase |> uri.path_segments {
    [] -> AllTodos
    ["active"] -> ActiveTodos
    ["completed"] -> CompletedTodos
    _ -> NotFound
  }
}
