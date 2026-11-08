namespace LustreTodos.Feature.Todo

/// <summary>The slice's endpoints, grouped by HTTP method. Shared by <c>Program.fs</c> and the test
/// host so both route the same handlers.</summary>
module TodoEndpoints =
    open Oxpecker

    open LustreTodos.Data.Db

    let all (queryContext : QueryContextFactory) = [
        GET [ ReadAllTodos.endpoint queryContext; ReadTodo.endpoint queryContext ]
        POST [ CreateTodo.endpoint queryContext ]
        PATCH [ UpdateTodo.endpoint queryContext ]
        DELETE [ DeleteTodo.endpoint queryContext; DeleteCompletedTodos.endpoint queryContext ]
    ]
