namespace LustreTodos.Feature.Auth

module Login =
    open Microsoft.AspNetCore.Authentication
    open Oxpecker

    open LustreTodos.Shared.Auth

    /// <summary>The query parameter naming the page to return to.</summary>
    [<Literal>]
    let ReturnUrlParameter = "returnUrl"

    let private handler (baseReturnUrl : string) : EndpointHandler =
        fun ctx ->
            task {
                let returnUrl =
                    ctx.TryGetQueryValue ReturnUrlParameter
                    |> ReturnUrl.resolve baseReturnUrl

                if ctx.User.Identity.IsAuthenticated then
                    ctx.Response.Redirect returnUrl
                else
                    let props =
                        AuthenticationProperties (RedirectUri = returnUrl)

                    return! ctx.ChallengeAsync (Auth.oidcScheme, props)
            }

    let endpoint (baseReturnUrl : string) : Endpoint =
        route Auth.LoginPath (handler baseReturnUrl)
