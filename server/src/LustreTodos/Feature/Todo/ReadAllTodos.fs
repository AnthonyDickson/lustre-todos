namespace LustreTodos.Feature.Todo

module ReadAllTodos =
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
    open LustreTodos.Shared.Json
    open LustreTodos.Shared.RequestLogging

    [<Literal>]
    let Path = "/api/todos"

    let private getAll (queryContext : QueryContextFactory) (userId : string) =
        task {
            let! rows =
                selectTask queryContext {
                    for t in main.Todos do
                        select t
                        where (t.UserId = userId)
                }

            return rows |> List.ofSeq |> List.map Todo.fromRow
        }

    let private handler (queryContext : QueryContextFactory) : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let! userId = Auth.getUserId ctx
                let! items = getAll queryContext userId
                let log = RequestLog.fromContext ctx

                log.Info ($"Returned %i{List.length items} todos", LogProp.prop "count" (List.length items))
                do! Json.write ctx items
            })

    let private configureOperation (op : OpenApiOperation) _ _ =
        op.Summary <- "List all todos"
        op.Description <- "Returns every todo item in the store."
        op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
        Task.CompletedTask

    let endpoint (queryContext : QueryContextFactory) =
        route Path (handler queryContext)
        |> addOpenApi (
            OpenApiConfig (
                responseBodies = [|
                    ResponseBody typeof<Todo list>
                    ResponseBody (typeof<ApiError>, statusCode = 401)
                |],
                configureOperation = configureOperation
            )
        )
