namespace LustreTodos.Feature.Auth

module Login =
    open Microsoft.AspNetCore.Authentication
    open Oxpecker

    open LustreTodos.Shared.Auth

    let private handler (returnUrl : string) : EndpointHandler =
        fun ctx ->
            task {
                if ctx.User.Identity.IsAuthenticated then
                    ctx.Response.Redirect "/"
                else
                    let props = AuthenticationProperties (RedirectUri = returnUrl)
                    return! ctx.ChallengeAsync (Auth.oidcScheme, props)
            }

    let endpoint (returnUrl : string) : Endpoint =
        route Auth.LoginPath (handler returnUrl)
