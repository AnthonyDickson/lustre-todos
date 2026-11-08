namespace LustreTodos.Feature.Auth

module Logout =
    open Microsoft.AspNetCore.Authentication
    open Oxpecker

    open LustreTodos.Shared.Auth

    /// <summary>Clear the session cookie, then end the provider's session
    /// through <c>providerLogoutUrl</c>, its logout page. Without one the
    /// provider session outlives the app's, so the user lands on the client's
    /// signed out page rather than being signed straight back in.</summary>
    let private handler (providerLogoutUrl : string option) : EndpointHandler =
        fun ctx ->
            task {
                // Authelia 4.39 does not support RP-Initiated Logout (see
                // https://github.com/authelia/authelia/pull/11660), hence the
                // configured logout page. Once released, replace it with
                // ctx.SignOutAsync oidcScheme, which finds the provider's end
                // session endpoint through discovery.
                do! ctx.SignOutAsync Auth.cookieScheme

                ctx.Response.Redirect (
                    Option.defaultValue Auth.SignedOutPath providerLogoutUrl
                )
            }

    let endpoint (providerLogoutUrl : string option) : Endpoint =
        route Auth.LogoutPath (handler providerLogoutUrl)
