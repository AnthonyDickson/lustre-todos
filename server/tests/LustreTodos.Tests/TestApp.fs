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

    let principal =
        let identity = ClaimsIdentity ([ Claim ("sub", userId) ], "test")
        ClaimsPrincipal identity

type TestAppConfig = {
    EndpointProviders : (string -> Oxpecker.RoutingTypes.Endpoint seq) list
    CleanTables : string list
}

module TestAppConfig =
    open LustreTodos
    open LustreTodos.Db

    let empty = {
        EndpointProviders = []
        CleanTables = []
    }

    let withTodos (config : TestAppConfig) = {
        config with
            EndpointProviders =
                (fun connStr -> QueryContextFactory.Create connStr |> Todos.Api.endpoints)
                :: config.EndpointProviders
            CleanTables = "Todos" :: config.CleanTables
    }

type TestApp = {
    Client : HttpClient
    CleanDatabase : unit -> unit
    Dispose : unit -> unit
} with

    interface IDisposable with
        member this.Dispose () = this.Dispose ()

module TestApp =
    open LustreTodos.RequestLogging
    open LustreTodos.Todos

    /// Create an app server with an in-memory SQLite database
    let create (config : TestAppConfig) =
        // In-memory database shared by every connection through SQLite's shared cache. The keeper
        // connection must stay open for the lifetime of the app — the in-memory DB is dropped when
        // the last connection to it closes. Each query opens its own connection, so disposing a
        // QueryContext (which closes its connection) doesn't lose the data.
        let name = $"test-todos-{Guid.NewGuid ()}"

        let connectionString =
            LustreTodos.Config.Config.withForeignKeys $"Data Source=file:{name}?mode=memory&cache=shared"

        let keeper = new SqliteConnection (connectionString)
        keeper.Open ()

        let endpoints =
            config.EndpointProviders
            |> Seq.collect (fun provider -> provider connectionString)

        let result =
            DbUp.DeployChanges.To
                .SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(typeof<Todo>.Assembly)
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
                                    ctx.User <- TestClaims.principal
                                    return! next.Invoke ()
                                }
                                :> Task)
                            |> ignore

                            app.UseRouting().UseOxpecker endpoints |> ignore)
                    |> ignore)
                .Build ()

        host.StartAsync().GetAwaiter().GetResult ()

        let client = host.GetTestClient ()

        let cleanDatabase () =
            use conn = new SqliteConnection (connectionString)
            conn.Open ()

            for table in config.CleanTables do
                use cmd = conn.CreateCommand ()
                cmd.CommandText <- $"DELETE FROM {table}"
                cmd.ExecuteNonQuery () |> ignore

        let dispose () =
            client.Dispose ()
            host.Dispose ()
            keeper.Dispose ()

        {
            Client = client
            CleanDatabase = cleanDatabase
            Dispose = dispose
        }
