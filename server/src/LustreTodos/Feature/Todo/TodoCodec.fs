namespace LustreTodos.Feature.Todo

/// <summary>Maps between the <c>Todos</c> row and the domain type. Rows are trusted, so reading one
/// does not validate it again.</summary>
module TodoCodec =
    open System

    open LustreTodos.Data.Db

    let fromRow (row : main.Todos) : Todo = {
        Id = row.Id
        Title = TodoTitle.unsafeFromString row.Title
        Completed = row.Completed
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt).UtcDateTime
    }

    let toRow (userId : string) (todo : Todo) : main.Todos = {
        Id = todo.Id
        UserId = userId
        Title = TodoTitle.value todo.Title
        Completed = todo.Completed
        CreatedAt = DateTimeOffset(todo.CreatedAt).ToUnixTimeSeconds ()
    }
