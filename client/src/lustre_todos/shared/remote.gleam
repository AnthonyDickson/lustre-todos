//// The lifecycle of data a page fetches from the server.

pub type Remote(a) {
  /// No data yet.
  Loading
  Loaded(a)
  /// The fetch failed and there is nothing to show; the page offers a retry.
  Failed
}

/// Apply `f` to loaded data. `Loading` and `Failed` are returned unchanged, so a
/// change made while nothing is loaded is dropped; the next fetch brings it in.
pub fn map(remote: Remote(a), f: fn(a) -> b) -> Remote(b) {
  case remote {
    Loaded(value) -> Loaded(f(value))
    Loading -> Loading
    Failed -> Failed
  }
}

/// The loaded data, or `default` while loading or after a failure.
pub fn unwrap(remote: Remote(a), or default: a) -> a {
  case remote {
    Loaded(value) -> value
    Loading | Failed -> default
  }
}
