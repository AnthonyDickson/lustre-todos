import gleam/list
import lustre/attribute
import lustre/element.{type Element}
import lustre/element/html
import lustre/event
import youid/uuid.{type Uuid}

pub type ToastLevel {
  Info
  Warning
  Error
}

pub type Toast {
  Toast(id: Uuid, title: String, body: String, level: ToastLevel)
}

fn level_to_class(level: ToastLevel) -> String {
  case level {
    Info -> "border-l-blue-400/40"
    Warning -> "border-l-amber-400/40"
    Error -> "border-l-red-400/60"
  }
}

fn level_to_role(level: ToastLevel) -> String {
  case level {
    Info | Warning -> "status"
    Error -> "alert"
  }
}

/// Render a toast. The toast should be rendered in a container with a fixed position.
/// See `view_with_container`.
pub fn view(toast: Toast, on_dismiss: fn(Uuid) -> msg) -> Element(msg) {
  html.div(
    [
      attribute.class(
        "pointer-events-auto bg-gray-50 border border-gray-200 border-l-4 "
        <> level_to_class(toast.level)
        <> " shadow-lg p-4 max-w-sm animate-[toast-in_0.3s_ease-out]",
      ),
      attribute.role(level_to_role(toast.level)),
    ],
    [
      html.div([attribute.class("flex justify-between items-start gap-3")], [
        html.div([], [
          html.p([attribute.class("text-sm font-medium text-gray-600")], [
            html.text(toast.title),
          ]),
          html.p([attribute.class("text-sm text-gray-500 mt-1")], [
            html.text(toast.body),
          ]),
        ]),
        html.button(
          [
            attribute.class(
              "text-gray-300 hover:text-gray-500 shrink-0 text-lg leading-none cursor-pointer",
            ),
            attribute.aria_label("Dismiss"),
            event.on_click(on_dismiss(toast.id)),
          ],
          [html.text("x")],
        ),
      ]),
    ],
  )
}

/// Render many toasts wrapped in a container with a fixed position in the top right of the page.
pub fn view_with_container(
  toasts: List(Toast),
  on_dismissed: fn(Uuid) -> msg,
) -> Element(msg) {
  html.div(
    [
      attribute.class(
        "fixed top-4 right-4 z-50 flex flex-col gap-2 pointer-events-none",
      ),
    ],
    list.map(toasts, view(_, on_dismissed)),
  )
}
