//// The server's login and logout endpoints. They are full page loads, not
//// SPA routes: a link to one would be caught by the client's router, so the
//// views reach them through forms or `effect.Redirect`.

import gleam/uri

pub const login = "/login"

pub const logout = "/logout"

/// The login path, returning to `url`, a path on this site, once logged in.
pub fn login_returning_to(url: String) -> String {
  login <> "?" <> uri.query_to_string([#("returnUrl", url)])
}
