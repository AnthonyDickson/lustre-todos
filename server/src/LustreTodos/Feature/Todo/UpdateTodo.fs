namespace LustreTodos.Feature.Todo

module UpdateTodo =
    open System
    open System.Collections.Generic
    open System.ComponentModel.DataAnnotations
    open System.Threading.Tasks

    open FsToolkit.ErrorHandling
    open Microsoft.OpenApi
    open Oxpecker
    open Oxpecker.OpenApi
    open SqlHydra.Query

    open LustreTodos.Data.Db
    open LustreTodos.Shared.ApiError
    open LustreTodos.Shared.Auth
    open LustreTodos.Shared.DomainError
    open LustreTodos.Shared.Endpoint
    open LustreTodos.Shared.Json
    open LustreTodos.Shared.RequestLogging

    /// <summary>Payload for updating an existing todo item.</summary>
    type UpdateTodoRequest = {
        /// <summary>The new title for the todo.</summary>
        [<MinLength(1)>]
        [<MaxLength(TodoTitle.MaxLength)>]
        Title : string

        /// <summary>Whether the todo is now completed.</summary>
        Completed : bool
    }

    [<Literal>]
    let Path = "/api/todos/{%O:guid}"

    let private update
        (queryContext : QueryContextFactory)
        (userId : string)
        (id : Guid)
        (title : TodoTitle)
        (completed : bool)
        =
        task {
            let title = TodoTitle.value title
            use! shared = queryContext.OpenContextAsync ()
            shared.BeginTransaction ()

            let! _rowsAffected =
                updateTask shared {
                    for t in main.Todos do
                        set t.Title title
                        set t.Completed completed
                        where (t.Id = id && t.UserId = userId)
                }

            let! result =
                selectTask shared {
                    for t in main.Todos do
                        where (t.Id = id && t.UserId = userId)
                        tryHead
                }

            shared.CommitTransaction ()

            return result |> Option.map TodoCodec.fromRow
        }

    let private handler (queryContext : QueryContextFactory) (id : Guid) : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let log = RequestLog.fromContext ctx
                let! (req : UpdateTodoRequest) = Json.read ctx
                let! userId = Auth.getUserId ctx

                let! title = TodoTitle.create req.Title
                let! updated = update queryContext userId id title req.Completed

                match updated with
                | Some updated ->
                    log.Info ($"Updated todo %O{id}", LogProp.prop "todoId" (id.ToString ()))
                    do! Json.write ctx (TodoResponse.fromDomain updated)
                | None ->
                    log.Warn ($"Todo %O{id} not found", LogProp.prop "todoId" (id.ToString ()))
                    return! Error (NotFound $"Todo %O{id} not found")
            })

    let private configureOperation (op : OpenApiOperation) _ _ =
        op.Summary <- "Update a todo"
        op.Description <- "Replaces the title and completed flag of an existing todo."
        op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
        Task.CompletedTask

    let endpoint (queryContext : QueryContextFactory) =
        routef Path (handler queryContext)
        |> addOpenApi (
            OpenApiConfig (
                requestBody = RequestBody typeof<UpdateTodoRequest>,
                responseBodies = [|
                    ResponseBody typeof<TodoResponse>
                    ResponseBody (typeof<ApiError>, statusCode = 400)
                    ResponseBody (typeof<ApiError>, statusCode = 401)
                    ResponseBody (typeof<ApiError>, statusCode = 404)
                |],
                configureOperation = configureOperation
            )
        )
