namespace LustreTodos.Feature.Auth

module Logout =
    open Microsoft.AspNetCore.Authentication
    open Oxpecker

    open LustreTodos.Shared.Auth

    let private handler : EndpointHandler =
        fun ctx ->
            task {
                // Currently Authelia does not support RP-Initiated Logout
                // (see https://github.com/authelia/authelia/pull/11660). Once released,
                // replace with: ctx.SignOutAsync cookieScheme then
                // ctx.SignOutAsync (oidcScheme, AuthenticationProperties (RedirectUri = returnUrl))
                return! ctx.SignOutAsync (Auth.cookieScheme, AuthenticationProperties (RedirectUri = "/"))
            }

    let endpoint : Endpoint = route Auth.LogoutPath handler
