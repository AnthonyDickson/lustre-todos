namespace LustreTodos.Tests.Todo

open System
open System.Net
open System.Net.Http
open System.Text
open Expecto
open LustreTodos.Shared.ApiError
open LustreTodos.Shared.Coders
open LustreTodos.Feature.Todo
open LustreTodos.Feature.Todo.UpdateTodo
open LustreTodos.Tests

module TodosTests =
    let private todo (id : Guid) (title : string) (completed : bool) : CreateTodo.CreateTodoRequest = {
        Id = id
        Title = title
        Completed = completed
        CreatedAt = DateTime.UtcNow
    }

    let private postJson (client : HttpClient) (url : string) (value : 'T) =
        let json = Encode.toString value
        let content = new StringContent (json, Encoding.UTF8, "application/json")
        client.PostAsync (url, content)

    let private patchJson (client : HttpClient) (url : string) (value : 'T) =
        let json = Encode.toString value
        let content = new StringContent (json, Encoding.UTF8, "application/json")
        client.PatchAsync (url, content)

    let newApp () =
        TestApp.create (TestAppConfig.empty |> TestAppConfig.withTodos)

    [<Tests>]
    let tests =
        testList "Todos" [
            testCaseAsync "GET /api/todos returns empty list when no todos exist"
            <| async {
                use app = newApp ()

                let! response = app.Client.GetAsync "/api/todos" |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"

                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask
                let result = Decode.fromString<TodoResponse list> body

                Expect.equal result (Ok []) "body should be empty list"
            }

            testCaseAsync "GET /api/todos returns seeded todos"
            <| async {
                use app = newApp ()

                let expected = todo (Guid.NewGuid ()) "Buy milk" false
                let! _ = postJson app.Client "/api/todos" expected |> Async.AwaitTask

                let! response = app.Client.GetAsync "/api/todos" |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"

                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<TodoResponse list> body with
                | Ok [ item ] ->
                    Expect.equal expected.Id item.Id "id should match"
                    Expect.equal expected.Title item.Title "title should match"
                    Expect.equal expected.Completed item.Completed "completed should match"
                | _ -> failtest "Expected one todo"
            }

            testCaseAsync "GET /api/todos/{id} returns the todo"
            <| async {
                use app = newApp ()

                let expected = todo (Guid.NewGuid ()) "Walk dog" true
                let! _ = postJson app.Client "/api/todos" expected |> Async.AwaitTask

                let! response = app.Client.GetAsync $"/api/todos/{expected.Id}" |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"

                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<TodoResponse> body with
                | Ok item ->
                    Expect.equal expected.Id item.Id "id should match"
                    Expect.equal expected.Title item.Title "title should match"
                    Expect.equal expected.Completed item.Completed "completed should match"
                | Error err -> failtest err
            }

            testCaseAsync "GET /api/todos/{id} returns 404 for missing todo"
            <| async {
                use app = newApp ()

                let! response = app.Client.GetAsync $"/api/todos/{Guid.NewGuid ()}" |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.NotFound "status code should be 404"
            }

            testCaseAsync "POST /api/todos creates a todo"
            <| async {
                use app = newApp ()

                let input = todo (Guid.NewGuid ()) "Learn F#" false

                let! response = postJson app.Client "/api/todos" input |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.Created "status code should be 201"

                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<TodoResponse> body with
                | Ok created ->
                    Expect.equal input.Id created.Id "id should match"
                    Expect.equal input.Title created.Title "title should match"
                    Expect.equal input.Completed created.Completed "completed should match"
                | Error err -> failtest err
            }

            testCaseAsync "POST /api/todos trims the title and rejects a blank one"
            <| async {
                use app = newApp ()

                let! padded =
                    postJson app.Client "/api/todos" (todo (Guid.NewGuid ()) "  Padded  " false)
                    |> Async.AwaitTask

                let! body = padded.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<TodoResponse> body with
                | Ok created -> Expect.equal created.Title "Padded" "the title should be trimmed"
                | Error err -> failtest err

                let! blank =
                    postJson app.Client "/api/todos" (todo (Guid.NewGuid ()) "   " false)
                    |> Async.AwaitTask

                Expect.equal blank.StatusCode HttpStatusCode.BadRequest "a blank title should be rejected"
            }

            testCaseAsync "POST /api/todos with an existing ID is rejected and keeps the original"
            <| async {
                use app = newApp ()

                let original = todo (Guid.NewGuid ()) "Original" false
                let! _ = postJson app.Client "/api/todos" original |> Async.AwaitTask

                let! response =
                    postJson app.Client "/api/todos" { original with Title = "Overwrite" }
                    |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.BadRequest "status code should be 400"

                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<ApiError> body with
                | Ok error ->
                    Expect.equal
                        error.Details
                        $"A todo with ID {original.Id} already exists"
                        "details should name the ID"
                | Error err -> failtest err

                let! stored = app.Client.GetStringAsync $"/api/todos/{original.Id}" |> Async.AwaitTask

                match Decode.fromString<TodoResponse> stored with
                | Ok stored -> Expect.equal stored.Title original.Title "the original should be kept"
                | Error err -> failtest err
            }

            testCaseAsync "PATCH /api/todos/{id} updates a todo"
            <| async {
                use app = newApp ()

                let original = todo (Guid.NewGuid ()) "Old title" false
                let! _ = postJson app.Client "/api/todos" original |> Async.AwaitTask

                let update : UpdateTodoRequest = {
                    Title = "New title"
                    Completed = true
                }

                let! response = patchJson app.Client $"/api/todos/{original.Id}" update |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"

                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<TodoResponse> body with
                | Ok updated ->
                    Expect.equal original.Id updated.Id "id should match"
                    Expect.equal update.Title updated.Title "title should match"
                    Expect.equal update.Completed updated.Completed "completed should match"
                | Error err -> failtest err
            }

            testCaseAsync "DELETE /api/todos/{id} removes the todo"
            <| async {
                use app = newApp ()

                let item = todo (Guid.NewGuid ()) "To delete" false
                let! _ = postJson app.Client "/api/todos" item |> Async.AwaitTask

                let! deleteResponse = app.Client.DeleteAsync $"/api/todos/{item.Id}" |> Async.AwaitTask

                Expect.equal deleteResponse.StatusCode HttpStatusCode.NoContent "delete status should be 204"

                let! getResponse = app.Client.GetAsync $"/api/todos/{item.Id}" |> Async.AwaitTask

                Expect.equal getResponse.StatusCode HttpStatusCode.NotFound "get after delete should be 404"
            }

            testCaseAsync "DELETE /api/todos/completed removes only completed todos"
            <| async {
                use app = newApp ()

                let completed = todo (Guid.NewGuid ()) "Done task" true
                let active = todo (Guid.NewGuid ()) "Active task" false
                let! _ = postJson app.Client "/api/todos" completed |> Async.AwaitTask
                let! _ = postJson app.Client "/api/todos" active |> Async.AwaitTask

                let! deleteResponse = app.Client.DeleteAsync "/api/todos/completed" |> Async.AwaitTask

                Expect.equal deleteResponse.StatusCode HttpStatusCode.NoContent "delete status should be 204"

                let! response = app.Client.GetAsync "/api/todos" |> Async.AwaitTask
                let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

                match Decode.fromString<TodoResponse list> body with
                | Ok items ->
                    Expect.equal (List.length items) 1 "should have 1 remaining todo"
                    Expect.equal active.Id items[0].Id "remaining todo id should match"
                | Error err -> failtest err
            }
        ]
