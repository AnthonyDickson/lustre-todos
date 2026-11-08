namespace LustreTodos.Feature.Todo

module DeleteCompletedTodos =
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
    open LustreTodos.Shared.Endpoint
    open LustreTodos.Shared.RequestLogging

    [<Literal>]
    let Path = "/api/todos/completed"

    let private deleteCompleted (queryContext : QueryContextFactory) (userId : string) =
        deleteTask queryContext {
            for t in main.Todos do
                where (t.Completed && t.UserId = userId)
        }

    let private handler (queryContext : QueryContextFactory) : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let log = RequestLog.fromContext ctx
                let! userId = Auth.getUserId ctx
                let! rowsAffected = deleteCompleted queryContext userId

                log.Info ($"Deleted %i{rowsAffected} completed todos", LogProp.prop "count" rowsAffected)
                ctx.SetStatusCode 204
            })

    let private configureOperation (op : OpenApiOperation) _ _ =
        op.Summary <- "Delete all completed todos"
        op.Description <- "Permanently removes all completed todos. Returns 204 on success."
        op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
        Task.CompletedTask

    let endpoint (queryContext : QueryContextFactory) =
        route Path (handler queryContext)
        |> addOpenApi (
            OpenApiConfig (
                responseBodies = [|
                    ResponseBody (typeof<unit>, statusCode = 204)
                    ResponseBody (typeof<ApiError>, statusCode = 401)
                |],
                configureOperation = configureOperation
            )
        )
