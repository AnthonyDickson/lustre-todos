namespace LustreTodos.Todos

open System
open System.ComponentModel.DataAnnotations

/// <summary>Limits shared by the Todo validation rules and their OpenAPI schema annotations.</summary>
module TodoLimits =
    [<Literal>]
    let internal MaxTitleLength = 256

/// <summary>A todo item stored in the database.</summary>
type Todo = {
    /// <summary>Unique identifier for the todo item.</summary>
    Id : Guid

    /// <summary>The title or description of the todo.</summary>
    [<MinLength(1)>]
    [<MaxLength(TodoLimits.MaxTitleLength)>]
    Title : string

    /// <summary>Whether the todo has been completed.</summary>
    Completed : bool

    /// <summary>UTC timestamp when the todo was created.</summary>
    CreatedAt : DateTime
}

module Todo =
    open LustreTodos.Db

    let fromRow (row : main.Todos) : Todo = {
        Id = row.Id
        Title = row.Title
        Completed = row.Completed
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt).UtcDateTime
    }

    let toRow (todo : Todo) (userId : string) : main.Todos = {
        Id = todo.Id
        UserId = userId
        Title = todo.Title
        Completed = todo.Completed
        CreatedAt = DateTimeOffset(todo.CreatedAt).ToUnixTimeSeconds ()
    }

/// <summary>Payload for updating an existing todo item.</summary>
type UpdateTodoRequest = {
    /// <summary>The new title for the todo.</summary>
    [<MinLength(1)>]
    [<MaxLength(TodoLimits.MaxTitleLength)>]
    Title : string

    /// <summary>Whether the todo is now completed.</summary>
    Completed : bool
}

module Validation =
    open LustreTodos.DomainError

    let private nonEmpty (title : string) =
        if String.IsNullOrWhiteSpace title then
            Error (ValidationFailed "Title cannot be null or just whitespace")
        else
            Ok title

    let private acceptableLength (title : string) =
        if title.Length > TodoLimits.MaxTitleLength then
            Error (
                ValidationFailed
                    $"Title is too long. Titles must be at most \
                    %i{TodoLimits.MaxTitleLength} characters, but got %i{title.Length}"
            )
        else
            Ok title

    /// <summary>Trim whitespace and then validate a Todo title. Returns the trimmed title.</summary>
    let validateAndTrimTitle (title : string) =
        title.Trim () |> nonEmpty |> Result.bind acceptableLength

    let validate (todo : Todo) =
        validateAndTrimTitle todo.Title
        |> Result.map (fun trimmedTitle -> { todo with Title = trimmedTitle })

module Api =
    open System.Collections.Generic
    open System.Threading.Tasks

    open FsToolkit.ErrorHandling
    open Microsoft.OpenApi
    open Oxpecker
    open Oxpecker.OpenApi
    open SqlHydra.Query

    open LustreTodos.ApiError
    open LustreTodos.Auth
    open LustreTodos.Db
    open LustreTodos.DomainError
    open LustreTodos.Endpoint
    open LustreTodos.Json
    open LustreTodos.RequestLogging

    module GetAll =
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

        let endpoint (queryContext : QueryContextFactory) =
            route Path (handler queryContext)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody typeof<Todo list>
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "List all todos"
                            op.Description <- "Returns every todo item in the store."
                            op.Tags <- HashSet [ OpenApiTagReference "Todos" ]

                            Task.CompletedTask
                )
            )

    module Get =
        [<Literal>]
        let Path = "/api/todos/{%O:guid}"

        let private get (queryContext : QueryContextFactory) (id : Guid) (userId : string) =
            task {
                let! result =
                    selectTask queryContext {
                        for t in main.Todos do
                            where (t.Id = id && t.UserId = userId)
                            tryHead
                    }

                return result |> Option.map Todo.fromRow
            }

        let private handler (queryContext : QueryContextFactory) (id : Guid) : EndpointHandler =
            Endpoint.handler (fun ctx ->
                taskResult {
                    let! userId = Auth.getUserId ctx
                    let! todo = get queryContext id userId
                    let log = RequestLog.fromContext ctx

                    match todo with
                    | Some item ->
                        log.Info ($"Returned todo %O{id}", LogProp.prop "todoId" (id.ToString ()))
                        do! Json.write ctx item
                    | None ->
                        log.Warn ($"Todo %O{id} not found", LogProp.prop "todoId" (id.ToString ()))
                        return! Error (NotFound $"Todo %O{id} not found")
                })

        let endpoint (queryContext : QueryContextFactory) =
            routef Path (handler queryContext)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody typeof<Todo>
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Get a todo by ID"
                            op.Description <- "Returns a single todo item, or 404 if not found."
                            op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
                            Task.CompletedTask
                )
            )

    module Create =
        [<Literal>]
        let Path = "/api/todos"

        let private insert (queryContext : QueryContextFactory) (todo : Todo) (userId : string) =
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

                    let! () = insert queryContext todo userId
                    log.Info ($"Created todo %O{todo.Id}", LogProp.prop "todoId" (todo.Id.ToString ()))
                    ctx.SetStatusCode 201
                    do! Json.write ctx todo
                })

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
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Create a todo"
                            op.Description <- "Creates a new todo item and returns it with status 201."
                            op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
                            Task.CompletedTask
                )
            )

    module Update =
        [<Literal>]
        let Path = "/api/todos/{%O:guid}"

        let private update
            (queryContext : QueryContextFactory)
            (id : Guid)
            (userId : string)
            (title : string)
            (completed : bool)
            =
            task {
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

                return result |> Option.map Todo.fromRow
            }

        let private handler (queryContext : QueryContextFactory) (id : Guid) : EndpointHandler =
            Endpoint.handler (fun ctx ->
                taskResult {
                    let log = RequestLog.fromContext ctx
                    let! (req : UpdateTodoRequest) = Json.read ctx
                    let! userId = Auth.getUserId ctx

                    let! title = Validation.validateAndTrimTitle req.Title
                    let! updated = update queryContext id userId title req.Completed

                    match updated with
                    | Some updated ->
                        log.Info ($"Updated todo %O{id}", LogProp.prop "todoId" (id.ToString ()))
                        do! Json.write ctx updated
                    | None ->
                        log.Warn ($"Todo %O{id} not found", LogProp.prop "todoId" (id.ToString ()))
                        return! Error (NotFound $"Todo %O{id} not found")
                })

        let endpoint (queryContext : QueryContextFactory) =
            routef Path (handler queryContext)
            |> addOpenApi (
                OpenApiConfig (
                    requestBody = RequestBody typeof<UpdateTodoRequest>,
                    responseBodies = [|
                        ResponseBody typeof<Todo>
                        ResponseBody (typeof<ApiError>, statusCode = 400)
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Update a todo"
                            op.Description <- "Replaces the title and completed flag of an existing todo."
                            op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
                            Task.CompletedTask
                )
            )

    module Delete =
        [<Literal>]
        let Path = "/api/todos/{%O:guid}"

        let delete (queryContext : QueryContextFactory) (id : Guid) (userId : string) =
            task {
                let! rows =
                    deleteTask queryContext {
                        for t in main.Todos do
                            where (t.Id = id && t.UserId = userId)
                    }

                return rows > 0
            }

        let private handler (queryContext : QueryContextFactory) (id : Guid) : EndpointHandler =
            Endpoint.handler (fun ctx ->
                taskResult {
                    let log = RequestLog.fromContext ctx
                    let! userId = Auth.getUserId ctx
                    let! deleted = delete queryContext id userId

                    if deleted then
                        log.Info ($"Deleted todo %O{id}", LogProp.prop "todoId" (id.ToString ()))
                        ctx.SetStatusCode 204
                    else
                        log.Warn ($"Todo %O{id} not found", LogProp.prop "todoId" (id.ToString ()))
                        return! Error (NotFound $"Todo %O{id} not found")
                })

        let endpoint (queryContext : QueryContextFactory) =
            routef Path (handler queryContext)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody (typeof<unit>, statusCode = 204)
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                        ResponseBody (typeof<ApiError>, statusCode = 404)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Delete a todo"
                            op.Description <- "Permanently removes a todo. Returns 204 on success."
                            op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
                            Task.CompletedTask
                )
            )

    module DeleteCompleted =
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

        let endpoint (queryContext : QueryContextFactory) =
            route Path (handler queryContext)
            |> addOpenApi (
                OpenApiConfig (
                    responseBodies = [|
                        ResponseBody (typeof<unit>, statusCode = 204)
                        ResponseBody (typeof<ApiError>, statusCode = 401)
                    |],
                    configureOperation =
                        fun op _ _ ->
                            op.Summary <- "Delete all completed todo"
                            op.Description <- "Permanently removes all completed todos. Returns 204 on success."
                            op.Tags <- HashSet [ OpenApiTagReference "Todos" ]
                            Task.CompletedTask
                )
            )

    let endpoints (ctx : QueryContextFactory) : Oxpecker.RoutingTypes.Endpoint seq = [
        GET [ GetAll.endpoint ctx; Get.endpoint ctx ]
        POST [ Create.endpoint ctx ]
        PATCH [ Update.endpoint ctx ]
        DELETE [ Delete.endpoint ctx; DeleteCompleted.endpoint ctx ]
    ]

/// This module defines the public API of the Todos feature slice
[<RequireQualifiedAccess>]
module Todos =
    open Oxpecker

    open LustreTodos.Auth
    open LustreTodos.Db

    let endpoints (queryContext : QueryContextFactory) =
        Api.endpoints queryContext |> Seq.map (addFilter Auth.requireAuth)
