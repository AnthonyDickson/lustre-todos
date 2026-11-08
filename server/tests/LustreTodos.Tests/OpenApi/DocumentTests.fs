namespace LustreTodos.Tests.OpenApi

module DocumentTests =
    open System.Text.Json
    open Expecto
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.TestHost
    open Microsoft.Extensions.DependencyInjection
    open Oxpecker

    open LustreTodos.Data.Db
    open LustreTodos.Feature.Status
    open LustreTodos.Feature.Todo
    open LustreTodos.Shared.Auth

    /// The OpenAPI document for the status endpoint and the todo slice, routed
    /// as the server routes them. Generating it never touches the database.
    let private document () =
        task {
            let builder = WebApplication.CreateBuilder ()
            builder.WebHost.UseTestServer () |> ignore
            builder.Services.AddRouting().AddOxpecker () |> ignore

            LustreTodos.Program.addOpenApiToBuilder builder {
                ClientId = "scalar-docs"
                AuthorizationUrl = "https://provider.test/authorize"
                TokenUrl = "https://provider.test/token"
            }

            use app = builder.Build ()
            let queryContext = QueryContextFactory.Create "Data Source=unused"

            let endpoints = [
                GET [
                    GetStatus.endpoint
                        "Data Source=unused"
                        "Development"
                        System.DateTimeOffset.UtcNow
                ]
                yield!
                    TodoEndpoints.all queryContext
                    |> List.map Auth.authenticated
            ]

            app.UseRouting().UseOxpecker endpoints |> ignore
            LustreTodos.Program.addOpenApiToApp app "scalar-docs"
            do! app.StartAsync ()

            let! body = app.GetTestClient().GetStringAsync "/openapi/v1.json"
            do! app.StopAsync ()
            return JsonDocument.Parse(body).RootElement
        }
        |> Async.AwaitTask

    let private at (keys : string list) (element : JsonElement) =
        keys
        |> List.fold (fun (e : JsonElement) key -> e.GetProperty key) element

    let private strings (element : JsonElement) = [
        for item in element.EnumerateArray () -> item.GetString ()
    ]

    [<Tests>]
    let tests =
        testList "OpenAPI document" [
            testCaseAsync "only authenticated operations declare security"
            <| async {
                let! doc = document ()

                let found, _ = doc.TryGetProperty "security"
                Expect.isFalse found "no document-level security"

                let found, _ =
                    (at [ "paths"; GetStatus.Path; "get" ] doc).TryGetProperty
                        "security"

                Expect.isFalse found "the status endpoint is open"

                let security =
                    at [ "paths"; CreateTodo.Path; "post"; "security" ] doc

                Expect.equal
                    [
                        for requirement in security.EnumerateArray () do
                            for scheme in requirement.EnumerateObject () ->
                                scheme.Name
                    ]
                    [ "scalarOAuth2" ]
                    "todo endpoints require the OAuth scheme"
            }

            testCaseAsync "required fields use the serialised names"
            <| async {
                let! doc = document ()
                let schemas = at [ "components"; "schemas" ] doc

                Expect.equal
                    (strings (at [ "CreateTodoRequest"; "required" ] schemas))
                    [ "id"; "title"; "completed"; "createdAt" ]
                    "request fields"

                Expect.equal
                    (strings (at [ "ApiError"; "required" ] schemas))
                    [ "error"; "details"; "requestId" ]
                    "option fields are not required"
            }
        ]
