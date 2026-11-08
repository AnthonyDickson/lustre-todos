namespace LustreTodos.Shared.Auth

open System

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Authentication.OpenIdConnect
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.OpenApi
open Oxpecker

open LustreTodos.Shared.Config
open LustreTodos.Shared.ApiError
open LustreTodos.Shared.Coders
open LustreTodos.Shared.DomainError

[<RequireQualifiedAccess>]
module Auth =
    [<Literal>]
    let LoginPath = "/login"

    [<Literal>]
    let LogoutPath = "/logout"

    let private policyName = "authenticated"

    let private polAuthScheme = "polAuth"
    let cookieScheme = CookieAuthenticationDefaults.AuthenticationScheme
    let oidcScheme = OpenIdConnectDefaults.AuthenticationScheme
    let private bearerScheme = "bearer"

    let oauthRequirement (doc : OpenApiDocument) : OpenApiSecurityRequirement =
        let schemeRef = OpenApiSecuritySchemeReference ("scalarOAuth2", doc)

        let requirement = OpenApiSecurityRequirement ()
        requirement[schemeRef] <- ResizeArray [ "openid"; "profile"; "email" ]
        requirement

    let private requirePolicy (policyName : string) : EndpointMiddleware =
        fun next ctx ->
            task {
                let authz = ctx.RequestServices.GetRequiredService<IAuthorizationService> ()

                let! result = authz.AuthorizeAsync (ctx.User, null, policyName)

                if result.Succeeded then
                    return! next ctx
                else
                    ctx.SetStatusCode 401
                    ctx.Response.ContentType <- "application/json; charset=utf-8"

                    return!
                        ctx.Response.WriteAsync (
                            Encode.toString {
                                Error = "Unauthorized"
                                Details = "Authentication required"
                                StatusCode = Some 401
                                RequestId = ctx.TraceIdentifier
                            }
                        )
            }

    let requireAuth : EndpointMiddleware = requirePolicy policyName

    let configureServices (services : IServiceCollection) (isDevelopment : bool) (oidc : OidcConfig) =
        services
            // ASP.NET Core only authenticates with DefaultScheme per request.
            // Since the SPA uses cookies but Scalar API docs use Bearer tokens,
            // a policy scheme routes to the right handler based on the auth header.
            .AddAuthentication(fun options ->
                options.DefaultScheme <- polAuthScheme
                options.DefaultChallengeScheme <- polAuthScheme)
            .AddPolicyScheme(
                polAuthScheme,
                "Combined auth",
                fun options ->
                    options.ForwardDefaultSelector <-
                        fun ctx ->
                            let authHeader = ctx.Request.Headers.Authorization.ToString ()

                            if authHeader.StartsWith ("Bearer ", StringComparison.OrdinalIgnoreCase) then
                                bearerScheme
                            else
                                cookieScheme
            )
            .AddCookie(
                cookieScheme,
                fun options ->
                    options.Cookie.SecurePolicy <-
                        if isDevelopment then
                            CookieSecurePolicy.SameAsRequest
                        else
                            CookieSecurePolicy.Always

                    options.Cookie.SameSite <- SameSiteMode.Lax
                    options.Cookie.HttpOnly <- true
                    options.Cookie.IsEssential <- true
                    options.LoginPath <- LoginPath
                    options.ExpireTimeSpan <- TimeSpan.FromHours 1
                    options.SlidingExpiration <- true
            )
            .AddOpenIdConnect(
                oidcScheme,
                fun options ->
                    options.Authority <- oidc.Authority
                    options.ClientId <- oidc.ClientId
                    options.ClientSecret <- oidc.ClientSecret
                    options.ResponseType <- "code"
                    options.CallbackPath <- oidc.CallbackPath
                    // Set this to true to get the name and email from Authelia/your identity provider
                    options.GetClaimsFromUserInfoEndpoint <- false
                    options.MapInboundClaims <- false
                    options.SaveTokens <- true

                    options.Scope.Add "openid" |> ignore
                    options.Scope.Add "profile" |> ignore
                    options.Scope.Add "email" |> ignore

                    if isDevelopment then
                        options.RequireHttpsMetadata <- false

                        options.BackchannelHttpHandler <-
                            new Net.Http.HttpClientHandler (
                                ServerCertificateCustomValidationCallback = fun _ _ _ _ -> true
                            )
            )
            .AddJwtBearer(
                bearerScheme,
                fun options ->
                    options.Authority <- oidc.Authority
                    options.MapInboundClaims <- false
                    options.RequireHttpsMetadata <- not isDevelopment

                    if isNull oidc.ValidAudiences || oidc.ValidAudiences.Length = 0 then
                        // No audiences configured — disable validation.
                        // Authelia does not include an aud claim in access tokens.
                        options.TokenValidationParameters.ValidateAudience <- false
                    else
                        options.TokenValidationParameters.ValidateAudience <- true
                        options.TokenValidationParameters.ValidAudiences <- oidc.ValidAudiences

                    if isDevelopment then
                        // Accept self-signed TLS certs for the backchannel OIDC discovery and JWKS fetches in dev.
                        options.BackchannelHttpHandler <-
                            new Net.Http.HttpClientHandler (
                                ServerCertificateCustomValidationCallback = fun _ _ _ _ -> true
                            )
            )
            .Services.AddAuthorization (fun options ->
                options.AddPolicy (
                    policyName,
                    fun policy ->
                        policy.AddAuthenticationSchemes(cookieScheme, bearerScheme).RequireAuthenticatedUser ()
                        |> ignore
                )
                |> ignore)
        |> ignore

    let getUserId (ctx : HttpContext) : Result<string, DomainError> =
        let userId = ctx.User.FindFirst "sub" |> Option.ofObj |> Option.map _.Value

        match userId with
        | Some userId -> Ok userId
        | None -> Error DomainError.Unauthorised
