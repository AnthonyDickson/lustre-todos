module LustreTodos.Program

open System.Collections.Generic
open System.ComponentModel.DataAnnotations
open System.Diagnostics
open System.IO
open System.Reflection
open System.Threading.Tasks

open DbUp
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.AspNetCore.Routing
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Microsoft.OpenApi
open Oxpecker
open Oxpecker.OpenApi
open Scalar.AspNetCore
open Serilog

open LustreTodos.Data.Db
open LustreTodos.Feature.Auth
open LustreTodos.Feature.Status
open LustreTodos.Feature.TestSupport
open LustreTodos.Feature.Todo
open LustreTodos.Shared.ApiError
open LustreTodos.Shared.Auth
open LustreTodos.Shared.Config
open LustreTodos.Shared.Json
open LustreTodos.Shared.Logging
open LustreTodos.Shared.OpenApi


let addOpenApiToBuilder
    (builder : WebApplicationBuilder)
    (ouath2 : OAuth2Config)
    =
    let oauth2AuthUrl = ouath2.AuthorizationUrl
    let oauth2TokenUrl = ouath2.TokenUrl

    builder.Services.AddOpenApi (fun options ->
        options.AddSchemaTransformer<FSharpOptionSchemaTransformer> ()
        |> ignore

        options.AddSchemaTransformer<OpenApi.FSharpRecordSchemaTransformer> ()
        |> ignore

        options.AddSchemaTransformer<OpenApi.XmlDocSchemaTransformer> ()
        |> ignore

        options.AddSchemaTransformer<OpenApi.SchemaHintTransformer> ()
        |> ignore

        options.AddSchemaTransformer<OpenApi.DecimalSchemaTransformer> ()
        |> ignore

        options.AddDocumentTransformer (fun doc _ _ ->
            if isNull doc.Components then
                doc.Components <- OpenApiComponents ()

            if isNull doc.Components.SecuritySchemes then
                doc.Components.SecuritySchemes <-
                    Dictionary<string, IOpenApiSecurityScheme> ()

            doc.Components.SecuritySchemes["bearerAuth"] <-
                OpenApiSecurityScheme (
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description =
                        "JWT access token from Authelia. Use the Scalar OAuth2 flow to obtain one."
                )

            doc.Components.SecuritySchemes["scalarOAuth2"] <-
                OpenApiSecurityScheme (
                    Type = SecuritySchemeType.OAuth2,
                    Flows =
                        OpenApiOAuthFlows (
                            AuthorizationCode =
                                OpenApiOAuthFlow (
                                    AuthorizationUrl =
                                        System.Uri oauth2AuthUrl,
                                    TokenUrl = System.Uri oauth2TokenUrl
                                )
                        )
                )

            Task.CompletedTask)
        |> ignore

        // Per operation, so endpoints outside Auth.authenticated (status) are
        // documented as open.
        options.AddOperationTransformer (fun operation context _ ->
            let requiresAuthentication =
                context.Description.ActionDescriptor.EndpointMetadata
                |> Seq.exists (fun metadata ->
                    metadata :? Auth.RequiresAuthentication)

            if requiresAuthentication then
                operation.Security <-
                    ResizeArray [ Auth.oauthRequirement context.Document ]

            Task.CompletedTask)
        |> ignore)
    |> ignore

let addOpenApiToApp (app : WebApplication) (clientId : string) =
    app.MapOpenApi () |> ignore

    app.MapScalarApiReference (fun opts ->
        opts
            .WithTitle("LustreTodos API")
            .WithTheme(ScalarTheme.DeepSpace)
            .WithDefaultHttpClient (ScalarTarget.Http, ScalarClient.Curl)
        |> ignore

        opts
            .AddPreferredSecuritySchemes([| "scalarOAuth2" |])
            .AddAuthorizationCodeFlow (
                "scalarOAuth2",
                fun flow ->
                    flow.ClientId <- clientId
                    flow.Pkce <- Pkce.Sha256
                    flow.SelectedScopes <- [| "openid"; "profile"; "email" |]
            )
        |> ignore)
    |> ignore

let private applyMigrations (connectionString : string) =
    let result =
        DeployChanges.To
            .SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly ())
            // A script that fails partway leaves the schema as it was. See
            // docs/database.md.
            .WithTransactionPerScript()
            .LogToConsole()
            .Build()
            .PerformUpgrade ()

    if not result.Successful then
        failwithf "Database migration failed: %O" result.Error

    use conn = new SqliteConnection (connectionString)
    conn.Open ()

    // WAL is stored in the database file, so setting it once applies to every
    // later connection. Foreign keys are per connection and are set in the
    // connection string (Config.withForeignKeys).
    use walCmd = conn.CreateCommand ()
    walCmd.CommandText <- "PRAGMA journal_mode = WAL"
    walCmd.ExecuteNonQuery () |> ignore

let private handleException
    (loggerFactory : ILoggerFactory)
    (next : RequestDelegate)
    : RequestDelegate =
    let logger = loggerFactory.CreateLogger "LustreTodos.Program"

    RequestDelegate (fun (ctx : HttpContext) ->
        task {
            try
                return! next.Invoke ctx
            with ex ->
                logger.LogError (ex, "An unhandled exception occurred")

                ctx.Response.StatusCode <- 500

                return!
                    Json.write ctx {
                        Error = "Internal Server Error"
                        Details = "An unexpected error occurred"
                        StatusCode = Some 500
                        RequestId = ctx.TraceIdentifier
                    }
        }
        :> Task)

let private apiNotFound : RequestDelegate =
    RequestDelegate (fun (ctx : HttpContext) ->
        ctx.Response.StatusCode <- 404

        Json.write ctx {
            Error = "Not Found"
            Details = $"No API endpoint matches {ctx.Request.Path}"
            StatusCode = Some 404
            RequestId = ctx.TraceIdentifier
        }
        :> Task)

let private configureForwardedHeaders
    (options : ForwardedHeadersOptions)
    : unit =
    options.ForwardedHeaders <-
        ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto

    // Trust common Docker/private network ranges, in addition to the default
    // loopback addresses (127.0.0.0/8 and ::1), which a proxy on the same host
    // uses when the server runs natively or with host networking.
    options.KnownIPNetworks.Add (System.Net.IPNetwork.Parse "10.0.0.0/8")
    options.KnownIPNetworks.Add (System.Net.IPNetwork.Parse "172.16.0.0/12")
    options.KnownIPNetworks.Add (System.Net.IPNetwork.Parse "192.168.0.0/16")

let private withAuth endpoints = Seq.map Auth.authenticated endpoints

let private buildEndpoints
    (connectionString : string)
    (loginReturnUrl : string)
    (providerLogoutUrl : string option)
    (app : WebApplication)
    =
    let queryContext = QueryContextFactory.Create connectionString

    let startedAt =
        System.DateTimeOffset (
            Process.GetCurrentProcess().StartTime.ToUniversalTime ()
        )

    let authEndpoints = [
        GET [ Login.endpoint loginReturnUrl ]
        // POST, so another site cannot log the user out with a link: the
        // SameSite=Lax cookie is not sent with its cross-site form posts.
        POST [ Logout.endpoint providerLogoutUrl ]
    ]

    let statusEndpoints = [
        GET [
            GetStatus.endpoint
                connectionString
                app.Environment.EnvironmentName
                startedAt
        ]
    ]

    let featureEndpoints = TodoEndpoints.all queryContext |> withAuth

    // Test-only helpers, available in Development only (like the Scalar docs
    // below) so they can never be reached against real data.
    let testEndpoints =
        if app.Environment.IsDevelopment () then
            [ DELETE [ ResetUserData.endpoint queryContext ] ] |> withAuth
        else
            Seq.empty

    Seq.concat [
        Seq.ofList authEndpoints
        Seq.ofList statusEndpoints
        featureEndpoints
        testEndpoints
    ]

let private configureBuilder
    (builder : WebApplicationBuilder)
    (config : AppConfig)
    : unit =
    let isDevelopment = builder.Environment.IsDevelopment ()

    Auth.configureServices builder.Services isDevelopment config.Oidc

    match config.DataProtection with
    | Some dataProtection ->
        // A fixed application name keeps the keys valid if the binary moves;
        // the default derives it from the content root path.
        builder.Services
            .AddDataProtection()
            .SetApplicationName("LustreTodos")
            .PersistKeysToFileSystem (DirectoryInfo dataProtection.KeysPath)
        |> ignore
    | None -> ()

    builder.Services.AddRouting().AddOxpecker () |> ignore

    builder.Services.Configure<ForwardedHeadersOptions>
        configureForwardedHeaders
    |> ignore

    builder.Services.Configure<HostOptions> (fun (options : HostOptions) ->
        options.ShutdownTimeout <- System.TimeSpan.FromSeconds 30L)
    |> ignore

    builder.WebHost.ConfigureKestrel (fun options ->
        options.Limits.MaxRequestBodySize <- 65536L)
    |> ignore

    builder.Host.UseSerilog (Logging.configure GetStatus.Path config.Logging)
    |> ignore

    match config.Oauth2 with
    | Some config when isDevelopment -> addOpenApiToBuilder builder config
    | _ -> ()

let private configureApp (app : WebApplication) (config : AppConfig) : unit =
    let isDevelopment = app.Environment.IsDevelopment ()

    match config.Oauth2 with
    | Some config when isDevelopment -> addOpenApiToApp app config.ClientId
    | _ -> ()

    app.Use (
        handleException (app.Services.GetRequiredService<ILoggerFactory> ())
    )
    |> ignore

    app.UseForwardedHeaders () |> ignore
    app.UseStaticFiles () |> ignore
    app.UseRouting () |> ignore
    app.UseAuthentication () |> ignore

    app.Use (
        Shared.RequestLogging.Middleware.requestLogging (
            (app.Services.GetRequiredService<Serilog.ILogger> ())
                .ForContext ("SourceContext", "LustreTodos.Request")
        )
    )
    |> ignore

    app.UseAuthorization () |> ignore

    app.UseOxpecker (
        buildEndpoints
            config.ConnectionString
            config.Login.ReturnUrl
            (Option.ofObj config.Oidc.LogoutUrl)
            app
    )
    |> ignore

    // Unknown API paths get a JSON 404 rather than the SPA's index.html. The
    // literal /api segment outranks the SPA fallback's catch-all. Like that
    // fallback it answers GET and HEAD only, so other methods keep their 405.
    app
        .MapFallback("/api/{**path}", apiNotFound)
        .WithMetadata (
            HttpMethodMetadata [| HttpMethods.Get; HttpMethods.Head |]
        )
    |> ignore

    // Run the vite dev server for accessing the SPA bundle in the dev
    // environment. The fallback is explicitly disabled to avoid surprising and
    // difficult to debug situations where the backend is not loading the code
    // you expect.
    if not isDevelopment then
        app.MapFallbackToFile "index.html" |> ignore

[<EntryPoint>]
let main (args : string array) : int =
    let builder = WebApplication.CreateBuilder args

    let config =
        try
            Config.load builder.Services builder.Configuration
        with :? OptionsValidationException as ex ->
            eprintfn
                "The server refused to start: configuration validation failed."

            ex.Failures |> Seq.iter (fun failure -> eprintfn "  - %s" failure)

            eprintfn
                "Fix the settings above and restart. \
                Values are read from appsettings.json or environment variables (e.g. Oidc__ClientSecret)."

            exit 1

    configureBuilder builder config

    let app = builder.Build ()
    configureApp app config

    applyMigrations config.ConnectionString

    app.Run ()
    0
