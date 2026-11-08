namespace LustreTodos.Tests.Todo

module EndpointTests =
    open System
    open System.Net
    open Expecto

    open LustreTodos.Feature.Todo
    open LustreTodos.Shared.ApiError
    open LustreTodos.Tests

    let private todo (id : Guid) (title : string) (completed : bool) : CreateTodo.CreateTodoRequest = {
        Id = id
        Title = title
        Completed = completed
        CreatedAt = DateTime.UtcNow
    }

    let private create app (request : CreateTodo.CreateTodoRequest) =
        async {
            let! response = TestHttp.postJson app CreateTodo.Path request |> Async.AwaitTask
            Expect.equal response.StatusCode HttpStatusCode.Created "creating a todo should return 201"
        }

    let private newApp () =
        TestApp.create (TestAppConfig.empty |> TestAppConfig.withTodos)

    [<Tests>]
    let tests =
        testList "Todos" [
            testCaseAsync "GET /api/todos returns empty list when no todos exist"
            <| async {
                use app = newApp ()

                let! response = app.Client.GetAsync ReadAllTodos.Path |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"
                let! todos = TestHttp.readJson<TodoResponse list> response
                Expect.equal todos [] "body should be empty list"
            }

            testCaseAsync "GET /api/todos returns seeded todos"
            <| async {
                use app = newApp ()

                let expected = todo (Guid.NewGuid ()) "Buy milk" false
                do! create app.Client expected

                let! response = app.Client.GetAsync ReadAllTodos.Path |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"

                match! TestHttp.readJson<TodoResponse list> response with
                | [ item ] ->
                    Expect.equal item.Id expected.Id "id should match"
                    Expect.equal item.Title expected.Title "title should match"
                    Expect.equal item.Completed expected.Completed "completed should match"
                | items -> failtest $"Expected one todo, got %A{items}"
            }

            testCaseAsync "GET /api/todos/{id} returns the todo"
            <| async {
                use app = newApp ()

                let expected = todo (Guid.NewGuid ()) "Walk dog" true
                do! create app.Client expected

                let! response =
                    app.Client.GetAsync (TestHttp.itemPath ReadTodo.Path expected.Id)
                    |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"
                let! item = TestHttp.readJson<TodoResponse> response
                Expect.equal item.Id expected.Id "id should match"
                Expect.equal item.Title expected.Title "title should match"
                Expect.equal item.Completed expected.Completed "completed should match"
            }

            testCaseAsync "GET /api/todos/{id} returns 404 for missing todo"
            <| async {
                use app = newApp ()

                let! response =
                    app.Client.GetAsync (TestHttp.itemPath ReadTodo.Path (Guid.NewGuid ()))
                    |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.NotFound "status code should be 404"
            }

            testCaseAsync "POST /api/todos creates a todo"
            <| async {
                use app = newApp ()

                let input = todo (Guid.NewGuid ()) "Learn F#" false

                let! response = TestHttp.postJson app.Client CreateTodo.Path input |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.Created "status code should be 201"
                let! created = TestHttp.readJson<TodoResponse> response
                Expect.equal created.Id input.Id "id should match"
                Expect.equal created.Title input.Title "title should match"
                Expect.equal created.Completed input.Completed "completed should match"
            }

            testCaseAsync "POST /api/todos trims the title and rejects a blank one"
            <| async {
                use app = newApp ()

                let! padded =
                    TestHttp.postJson app.Client CreateTodo.Path (todo (Guid.NewGuid ()) "  Padded  " false)
                    |> Async.AwaitTask

                let! created = TestHttp.readJson<TodoResponse> padded
                Expect.equal created.Title "Padded" "the title should be trimmed"

                let! blank =
                    TestHttp.postJson app.Client CreateTodo.Path (todo (Guid.NewGuid ()) "   " false)
                    |> Async.AwaitTask

                Expect.equal blank.StatusCode HttpStatusCode.BadRequest "a blank title should be rejected"
            }

            testCaseAsync "POST /api/todos with an existing ID is rejected and keeps the original"
            <| async {
                use app = newApp ()

                let original = todo (Guid.NewGuid ()) "Original" false
                do! create app.Client original

                // Another user: the ID is the primary key across every user.
                let! response =
                    TestHttp.postJson (app.ClientForUser "other-user") CreateTodo.Path {
                        original with
                            Title = "Overwrite"
                    }
                    |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.BadRequest "status code should be 400"
                let! error = TestHttp.readJson<ApiError> response

                Expect.equal error.Details $"A todo with ID {original.Id} already exists" "details should name the ID"

                let! stored =
                    app.Client.GetAsync (TestHttp.itemPath ReadTodo.Path original.Id)
                    |> Async.AwaitTask

                let! stored = TestHttp.readJson<TodoResponse> stored
                Expect.equal stored.Title original.Title "the original should be kept"
            }

            testCaseAsync "PATCH /api/todos/{id} updates a todo"
            <| async {
                use app = newApp ()

                let original = todo (Guid.NewGuid ()) "Old title" false
                do! create app.Client original

                let update : UpdateTodo.UpdateTodoRequest = {
                    Title = "New title"
                    Completed = true
                }

                let! response =
                    TestHttp.patchJson app.Client (TestHttp.itemPath UpdateTodo.Path original.Id) update
                    |> Async.AwaitTask

                Expect.equal response.StatusCode HttpStatusCode.OK "status code should be 200"
                let! updated = TestHttp.readJson<TodoResponse> response
                Expect.equal updated.Id original.Id "id should match"
                Expect.equal updated.Title update.Title "title should match"
                Expect.equal updated.Completed update.Completed "completed should match"
            }

            testCaseAsync "DELETE /api/todos/{id} removes the todo"
            <| async {
                use app = newApp ()

                let item = todo (Guid.NewGuid ()) "To delete" false
                do! create app.Client item

                let! deleteResponse =
                    app.Client.DeleteAsync (TestHttp.itemPath DeleteTodo.Path item.Id)
                    |> Async.AwaitTask

                Expect.equal deleteResponse.StatusCode HttpStatusCode.NoContent "delete status should be 204"

                let! getResponse = app.Client.GetAsync (TestHttp.itemPath ReadTodo.Path item.Id) |> Async.AwaitTask

                Expect.equal getResponse.StatusCode HttpStatusCode.NotFound "get after delete should be 404"
            }

            testCaseAsync "DELETE /api/todos/completed removes only completed todos"
            <| async {
                use app = newApp ()

                let completed = todo (Guid.NewGuid ()) "Done task" true
                let active = todo (Guid.NewGuid ()) "Active task" false
                do! create app.Client completed
                do! create app.Client active

                let! deleteResponse = app.Client.DeleteAsync DeleteCompletedTodos.Path |> Async.AwaitTask

                Expect.equal deleteResponse.StatusCode HttpStatusCode.NoContent "delete status should be 204"

                let! response = app.Client.GetAsync ReadAllTodos.Path |> Async.AwaitTask
                let! items = TestHttp.readJson<TodoResponse list> response
                Expect.equal (items |> List.map _.Id) [ active.Id ] "only the active todo should remain"
            }

            testCaseAsync "another user can neither see nor change a user's todos"
            <| async {
                use app = newApp ()
                let other = app.ClientForUser "other-user"

                let completed = todo (Guid.NewGuid ()) "Owned" true
                do! create app.Client completed
                let itemPath = TestHttp.itemPath ReadTodo.Path completed.Id

                let! read = other.GetAsync itemPath |> Async.AwaitTask
                Expect.equal read.StatusCode HttpStatusCode.NotFound "another user's read should be 404"

                let! list = other.GetStringAsync ReadAllTodos.Path |> Async.AwaitTask
                Expect.equal list "[]" "another user's list should be empty"

                let! update =
                    TestHttp.patchJson
                        other
                        itemPath
                        ({ Title = "Taken"; Completed = false } : UpdateTodo.UpdateTodoRequest)
                    |> Async.AwaitTask

                Expect.equal update.StatusCode HttpStatusCode.NotFound "another user's update should be 404"

                let! delete = other.DeleteAsync itemPath |> Async.AwaitTask
                Expect.equal delete.StatusCode HttpStatusCode.NotFound "another user's delete should be 404"

                let! _ = other.DeleteAsync DeleteCompletedTodos.Path |> Async.AwaitTask

                let! stillThere = app.Client.GetAsync itemPath |> Async.AwaitTask
                let! stillThere = TestHttp.readJson<TodoResponse> stillThere
                Expect.equal stillThere.Title completed.Title "the owner's todo should be unchanged"
            }
        ]
