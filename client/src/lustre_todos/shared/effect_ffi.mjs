export function loadFromStore(key) {
  const value = window.localStorage.getItem(key);
  return value ?? "";
}

export function saveToStore(key, value) {
  window.localStorage.setItem(key, value);
}

export function redirect(url) {
  window.location.assign(url);
}

// The router's notifier, set by `initRouting`. `pushUrl` and `replaceUrl`
// call it, since `history.pushState` and `replaceState` fire no `popstate`.
let notifyRoute = () => {};

export function initRouting(handler) {
  const notify = () => {
    handler(window.location.pathname + window.location.search + window.location.hash);
  };
  notifyRoute = notify;

  notify();

  window.addEventListener("popstate", (event) => {
    event.preventDefault();
    notify();
  });

  document.addEventListener("click", (event) => {
    const a = event.target.closest("a");
    if (!a?.href) return;
    if (!URL.canParse(a.href)) return;
    if (a.hasAttribute("download")) return;

    const url = new URL(a.href);
    if (url.origin !== window.location.origin) return;
    if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
    if (a.target === "_blank") return;

    event.preventDefault();
    history.pushState(null, "", url.pathname + url.search + url.hash);
    notify();

    // The browser normally scrolls on navigation — since we preventDefault,
    // handle it ourselves.
    window.requestAnimationFrame(() => {
      if (url.hash) {
        document.getElementById(url.hash.slice(1))?.scrollIntoView();
      } else {
        window.scrollTo(0, 0);
      }
    });
  });
}

export function pushUrl(url) {
  history.pushState(null, "", url);
  notifyRoute();
}

export function replaceUrl(url) {
  history.replaceState(null, "", url);
  notifyRoute();
}

export function setTitle(title) {
  document.title = title;
}

export function getOrigin() {
  return globalThis.location.origin;
}
