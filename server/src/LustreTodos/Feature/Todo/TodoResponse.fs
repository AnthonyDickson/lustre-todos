namespace LustreTodos.Feature.Todo

open System

/// <summary>A todo item.</summary>
type TodoResponse = {
    /// <summary>Unique identifier for the todo item.</summary>
    Id : Guid

    /// <summary>The title or description of the todo.</summary>
    Title : string

    /// <summary>Whether the todo has been completed.</summary>
    Completed : bool

    /// <summary>When the todo was created.</summary>
    CreatedAt : DateTime
}

module TodoResponse =
    let fromDomain (todo : Todo) : TodoResponse = {
        Id = todo.Id
        Title = TodoTitle.value todo.Title
        Completed = todo.Completed
        CreatedAt = todo.CreatedAt
    }
