namespace LustreTodos.Feature.Todo

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
    open LustreTodos.Data.Db

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

module Validation =
    open LustreTodos.Shared.DomainError

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
