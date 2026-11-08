namespace LustreTodos.Tests

open System
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Http
open Oxpecker

module private TestClaims =
    let userId = "test-user"

    /// The header a request can carry to act as a different user against the same database. Used by
    /// the cross-user scoping tests.
    let header = "X-Test-User"

    let principal (userId : string) =
        let identity = ClaimsIdentity ([ Claim ("sub", userId) ], "test")
        ClaimsPrincipal identity

type TestAppConfig = {
    EndpointProviders : (LustreTodos.Data.Db.QueryContextFactory -> Oxpecker.RoutingTypes.Endpoint list) list
}

/// Each `with*` routes a slice's production endpoint list, minus the auth filter.
module TestAppConfig =
    open LustreTodos.Data.Db
    open LustreTodos.Feature.Todo

    let empty = { EndpointProviders = [] }

    let private withEndpoints
        (provider : QueryContextFactory -> Oxpecker.RoutingTypes.Endpoint list)
        (config : TestAppConfig)
        =
        {
            EndpointProviders = provider :: config.EndpointProviders
        }

    let withTodos = withEndpoints TodoEndpoints.all

type TestApp = {
    Client : HttpClient
    /// A client authenticated as a different user against the same database.
    ClientForUser : string -> HttpClient
    Dispose : unit -> unit
} with

    interface IDisposable with
        member this.Dispose () = this.Dispose ()

module TestApp =
    open LustreTodos.Data.Db
    open LustreTodos.Shared.RequestLogging

    /// Serialises request-log dumps across concurrent tests so their output can't interleave.
    let private dumpLock = obj ()

    /// Messages can embed full stack traces; keep just the first line for readability.
    let private firstLine (message : string) =
        let idx = message.IndexOf '\n'
        if idx >= 0 then message.Substring (0, idx) else message

    /// Print the buffered request log to stderr, so a failing test shows the real server-side error
    /// (e.g. the SQLite exception) instead of an opaque 500 body.
    let private dumpRequestLog (ctx : HttpContext) =
        let entries = (RequestLog.fromContext ctx).Entries

        if not (List.isEmpty entries) then
            let sb = System.Text.StringBuilder ()
            sb.AppendLine () |> ignore

            let method = ctx.Request.Method
            let path = ctx.Request.Path.ToString ()
            let status = ctx.Response.StatusCode
            sb.AppendLine $"[TestApp] {method} {path} -> {status}" |> ignore

            for entry in entries do
                let level = LogLevel.toString entry.Level
                sb.AppendLine $"  [{level}] {firstLine entry.Message}" |> ignore

            // Write the whole block in one call under a lock so concurrent requests can't interleave
            // their output mid-line.
            lock dumpLock (fun () -> eprintf "%s" (sb.ToString ()))

    /// The `sub` claim to authenticate a request as: the `X-Test-User` header when present, otherwise
    /// the default test user.
    let private requestUser (ctx : HttpContext) =
        let header = ctx.Request.Headers[TestClaims.header]

        if header.Count > 0 && not (String.IsNullOrWhiteSpace header[0]) then
            string header[0]
        else
            TestClaims.userId

    /// Create an app server with an in-memory SQLite database
    let create (config : TestAppConfig) =
        // In-memory database shared by every connection through SQLite's shared cache. The keeper
        // connection must stay open for the lifetime of the app — the in-memory DB is dropped when
        // the last connection to it closes. Each query opens its own connection, so disposing a
        // QueryContext (which closes its connection) doesn't lose the data.
        let name = $"test-{Guid.NewGuid ()}"

        let connectionString =
            LustreTodos.Shared.Config.Config.withForeignKeys $"Data Source=file:{name}?mode=memory&cache=shared"

        let keeper = new SqliteConnection (connectionString)
        keeper.Open ()

        let queryContext = QueryContextFactory.Create connectionString

        let endpoints =
            config.EndpointProviders |> Seq.collect (fun provider -> provider queryContext)

        let result =
            DbUp.DeployChanges.To
                .SqliteDatabase(connectionString)
                // The migration scripts are embedded in the server assembly.
                .WithScriptsEmbeddedInAssembly(typeof<QueryContextFactory>.Assembly)
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade ()

        if not result.Successful then
            failwithf "Test database migration failed: %O" result.Error

        let host =
            HostBuilder()
                .ConfigureWebHost(fun webHostBuilder ->
                    webHostBuilder
                        .UseTestServer()
                        .ConfigureServices(fun services -> services.AddRouting().AddOxpecker () |> ignore)
                        .Configure (fun app ->
                            app.Use (fun (ctx : HttpContext) (next : Func<Task>) ->
                                task {
                                    ctx.Items[RequestLog.Key] <- RequestLog ()
                                    ctx.User <- TestClaims.principal (requestUser ctx)

                                    try
                                        return! next.Invoke ()
                                    finally
                                        if ctx.Response.StatusCode >= 500 then
                                            dumpRequestLog ctx
                                }
                                :> Task)
                            |> ignore

                            app.UseRouting().UseOxpecker endpoints |> ignore)
                    |> ignore)
                .Build ()

        host.StartAsync().GetAwaiter().GetResult ()

        let client = host.GetTestClient ()

        let clientForUser (userId : string) =
            let client = host.GetTestClient ()
            client.DefaultRequestHeaders.Add (TestClaims.header, userId)
            client

        let dispose () =
            client.Dispose ()
            host.Dispose ()
            keeper.Dispose ()

        {
            Client = client
            ClientForUser = clientForUser
            Dispose = dispose
        }
