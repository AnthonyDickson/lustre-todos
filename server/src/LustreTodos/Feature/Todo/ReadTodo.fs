namespace LustreTodos.Feature.Todo

module ReadTodo =
    open System
    open System.Collections.Generic
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

    [<Literal>]
    let Path = "/api/todos/{%O:guid}"

    let private get
        (queryContext : QueryContextFactory)
        (userId : string)
        (id : Guid)
        =
        task {
            let! result =
                selectTask queryContext {
                    for t in main.Todos do
                        where (t.Id = id && t.UserId = userId)
                        tryHead
                }

            return result |> Option.map TodoCodec.fromRow
        }

    let private handler
        (queryContext : QueryContextFactory)
        (id : Guid)
        : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let! userId = Auth.getUserId ctx
                let! todo = get queryContext userId id
                let log = RequestLog.fromContext ctx

                match todo with
                | Some item ->
                    log.Info (
                        $"Returned todo %O{id}",
                        LogProp.prop "todoId" (id.ToString ())
                    )

                    do! Json.write ctx (TodoResponse.fromDomain item)
                | None ->
                    log.Warn (
                        $"Todo %O{id} not found",
                        LogProp.prop "todoId" (id.ToString ())
                    )

                    return! Error (NotFound $"Todo %O{id} not found")
            })

    let private configureOperation (op : OpenApiOperation) _ _ =
        op.Summary <- "Get a todo by ID"
        op.Description <- "Returns a single todo item, or 404 if not found."
        op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
        Task.CompletedTask

    let endpoint (queryContext : QueryContextFactory) =
        routef Path (handler queryContext)
        |> addOpenApi (
            OpenApiConfig (
                responseBodies = [|
                    ResponseBody typeof<TodoResponse>
                    ResponseBody (typeof<ApiError>, statusCode = 401)
                    ResponseBody (typeof<ApiError>, statusCode = 404)
                |],
                configureOperation = configureOperation
            )
        )
