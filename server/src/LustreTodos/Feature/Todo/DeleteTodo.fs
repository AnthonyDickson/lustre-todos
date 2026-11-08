namespace LustreTodos.Feature.Todo

module DeleteTodo =
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
    open LustreTodos.Shared.RequestLogging

    [<Literal>]
    let Path = "/api/todos/{%O:guid}"

    let private delete
        (queryContext : QueryContextFactory)
        (userId : string)
        (id : Guid)
        =
        task {
            let! rows =
                deleteTask queryContext {
                    for t in main.Todos do
                        where (t.Id = id && t.UserId = userId)
                }

            return rows > 0
        }

    let private handler
        (queryContext : QueryContextFactory)
        (id : Guid)
        : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let log = RequestLog.fromContext ctx
                let! userId = Auth.getUserId ctx
                let! deleted = delete queryContext userId id

                if deleted then
                    log.Info (
                        $"Deleted todo %O{id}",
                        LogProp.prop "todoId" (id.ToString ())
                    )

                    ctx.SetStatusCode 204
                else
                    log.Warn (
                        $"Todo %O{id} not found",
                        LogProp.prop "todoId" (id.ToString ())
                    )

                    return! Error (NotFound $"Todo %O{id} not found")
            })

    let private configureOperation (op : OpenApiOperation) _ _ =
        op.Summary <- "Delete a todo"
        op.Description <- "Permanently removes a todo. Returns 204 on success."
        op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
        Task.CompletedTask

    let endpoint (queryContext : QueryContextFactory) =
        routef Path (handler queryContext)
        |> addOpenApi (
            OpenApiConfig (
                responseBodies = [|
                    ResponseBody (typeof<unit>, statusCode = 204)
                    ResponseBody (typeof<ApiError>, statusCode = 401)
                    ResponseBody (typeof<ApiError>, statusCode = 404)
                |],
                configureOperation = configureOperation
            )
        )
