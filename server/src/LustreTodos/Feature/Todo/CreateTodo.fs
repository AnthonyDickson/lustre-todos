namespace LustreTodos.Feature.Todo

module CreateTodo =
    open System
    open System.Collections.Generic
    open System.Threading.Tasks

    open FsToolkit.ErrorHandling
    open Microsoft.OpenApi
    open Oxpecker
    open Oxpecker.OpenApi
    open SqlHydra.Query

    open LustreTodos.Data
    open LustreTodos.Data.Db
    open LustreTodos.Shared.ApiError
    open LustreTodos.Shared.Auth
    open LustreTodos.Shared.Endpoint
    open LustreTodos.Shared.Json
    open LustreTodos.Shared.RequestLogging

    [<Literal>]
    let Path = "/api/todos"

    /// <summary>Verify that no todo has the given ID (the primary key). The key spans every user, so
    /// unlike other queries this one does not filter by user: an ID taken by another user would
    /// otherwise pass the check and fail the insert.</summary>
    let private requireIdIsAvailable (queryContext : QueryContextFactory) (id : Guid) =
        task {
            let! rowCount =
                selectTask queryContext {
                    for t in main.Todos do
                        where (t.Id = id)
                        count
                }

            return
                if rowCount = 0 then
                    Ok ()
                else
                    Error (ConstraintError $"A todo with ID %O{id} already exists")
        }

    let private insert (queryContext : QueryContextFactory) (userId : string) (todo : Todo) =
        task {
            let! _ =
                insertTask queryContext {
                    for t in main.Todos do
                        entity (Todo.toRow todo userId)
                }

            return ()
        }

    let private handler (queryContext : QueryContextFactory) : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let log = RequestLog.fromContext ctx
                let! userId = Auth.getUserId ctx
                let! (todo : Todo) = Json.read ctx
                let! todo = Validation.validate todo
                do! Constraints.requireOne (requireIdIsAvailable queryContext todo.Id)

                let! () = insert queryContext userId todo
                log.Info ($"Created todo %O{todo.Id}", LogProp.prop "todoId" (todo.Id.ToString ()))
                ctx.SetStatusCode 201
                do! Json.write ctx todo
            })

    let private configureOperation (op : OpenApiOperation) _ _ =
        op.Summary <- "Create a todo"
        op.Description <- "Creates a new todo item and returns it with status 201."
        op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
        Task.CompletedTask

    let endpoint (queryContext : QueryContextFactory) =
        route Path (handler queryContext)
        |> addOpenApi (
            OpenApiConfig (
                requestBody = RequestBody typeof<Todo>,
                responseBodies = [|
                    ResponseBody (typeof<Todo>, statusCode = 201)
                    ResponseBody (typeof<ApiError>, statusCode = 400)
                    ResponseBody (typeof<ApiError>, statusCode = 401)
                    ResponseBody (typeof<ApiError>, statusCode = 409)
                |],
                configureOperation = configureOperation
            )
        )
