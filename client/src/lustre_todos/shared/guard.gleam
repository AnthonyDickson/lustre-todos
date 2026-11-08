//// `use`-compatible early-return helpers for `Option` and `Result`.
////
//// These provide the same escape-hatch pattern as `gleam/bool.lazy_guard`:
//// extract the success value and continue, or short-circuit with a fallback
//// of any type — not constrained to `Option`/`Result` like `option.then` or
//// `result.try`.

import gleam/option.{type Option, None, Some}

/// Extract the value from `Some`, or short-circuit with `else_return` on `None`.
///
/// ```gleam
/// let foo = Some("foo")
///
/// use value <- guard.some_lazy(
///   in: foo,
///   else_return: fn() { "bar" },
/// )
///
/// let assert "foo" = value
/// // value is the unwrapped inner type — not wrapped in Option
/// ```
///
pub fn some_lazy(
  in option: Option(a),
  else_return return: fn() -> b,
  then cont: fn(a) -> b,
) -> b {
  case option {
    Some(value) -> cont(value)
    None -> return()
  }
}

/// Extract the value from `Some`, or short-circuit with `else_return` on `None`.
///
/// ```gleam
/// let foo = Some("foo")
///
/// use value <- guard.some(
///   in: foo,
///   else_return: "bar",
/// )
///
/// let assert "foo" = value
/// ```
///
pub fn some(
  in option: Option(a),
  else_return default: b,
  then cont: fn(a) -> b,
) -> b {
  case option {
    Some(value) -> cont(value)
    None -> default
  }
}

/// Extract the value from `Ok`, or short-circuit with `else_return` on `Error`.
///
/// ```gleam
/// let foo = Ok("foo")
///
/// use value <- guard.ok_lazy(
///   in: foo,
///   else_return: fn(_error) { "bar" },
/// )
///
/// let assert "foo" = value
/// ```
///
pub fn ok_lazy(
  in result: Result(a, e),
  else_return return: fn(e) -> b,
  then cont: fn(a) -> b,
) -> b {
  case result {
    Ok(value) -> cont(value)
    Error(err) -> return(err)
  }
}

/// Extract the value from `Ok`, or short-circuit with `else_return` on `Error`.
///
/// ```gleam
/// let foo = Ok("foo")
///
/// use value <- guard.ok(
///   in: foo,
///   else_return: "bar",
/// )
///
/// let assert "foo" = value
/// ```
pub fn ok(
  in result: Result(a, e),
  else_return default: b,
  then cont: fn(a) -> b,
) -> b {
  case result {
    Ok(value) -> cont(value)
    Error(_) -> default
  }
}
