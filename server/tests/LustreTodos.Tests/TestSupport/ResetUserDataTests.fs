namespace LustreTodos.Tests.TestSupport

module ResetUserDataTests =
    open System
    open System.Net
    open System.Net.Http
    open Expecto

    open LustreTodos.Feature.TestSupport
    open LustreTodos.Feature.Todo
    open LustreTodos.Tests

    let private newApp () =
        TestApp.create (
            TestAppConfig.empty
            |> TestAppConfig.withTodos
            |> TestAppConfig.withResetUserData
        )

    /// Give a user a todo.
    let private seed (client : HttpClient) =
        async {
            let request : CreateTodo.CreateTodoRequest = {
                Id = Guid.CreateVersion7 ()
                Title = "Seeded"
                Completed = false
                CreatedAt = DateTimeOffset.UtcNow
            }

            let! created =
                TestHttp.postJson client CreateTodo.Path request
                |> Async.AwaitTask

            Expect.equal
                created.StatusCode
                HttpStatusCode.Created
                "seeding a todo should return 201"
        }

    let private todoCount (client : HttpClient) =
        async {
            let! response = client.GetAsync ReadAllTodos.Path |> Async.AwaitTask
            let! todos = TestHttp.readJson<TodoResponse list> response
            return List.length todos
        }

    [<Tests>]
    let tests =
        testList "ResetUserData" [
            testCaseAsync
                "clears the current user's data and leaves other users' data"
            <| async {
                use app = newApp ()
                use otherUser = app.ClientForUser "other-user"
                do! seed app.Client
                do! seed otherUser

                let! response =
                    app.Client.DeleteAsync ResetUserData.Path |> Async.AwaitTask

                Expect.equal
                    response.StatusCode
                    HttpStatusCode.NoContent
                    "reset should return 204"

                let! count = todoCount app.Client
                Expect.equal count 0 "the user's todos should be deleted"

                let! count = todoCount otherUser
                Expect.equal count 1 "another user's todos should remain"
            }
        ]
