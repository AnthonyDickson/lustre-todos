import gleam/option.{type Option, Some}
import lustre_todos/shared/toast.{type ToastLevel, Error, Info}

/// Messages that are sent from child modules to the parent (root) module.
pub type OutMsg {
  /// Request the root app to display a toast and optionally dismiss it
  /// automatically after `dismiss_after_ms` milliseconds.
  /// If `dismiss_after_ms` is `None`, then the toast must be manually dismissed
  /// by the user.
  PageRequestedToast(
    title: String,
    body: String,
    level: ToastLevel,
    dismiss_after_ms: Option(Int),
  )
}

/// Ask the root app to show an auto-dismissing information toast.
pub fn info_toast(title: String, body: String) -> OutMsg {
  PageRequestedToast(
    title: title,
    body: body,
    level: Info,
    dismiss_after_ms: Some(5000),
  )
}

/// Ask the root app to show an auto-dismissing error toast.
pub fn error_toast(title: String, body: String) -> OutMsg {
  PageRequestedToast(
    title: title,
    body: body,
    level: Error,
    dismiss_after_ms: Some(5000),
  )
}
