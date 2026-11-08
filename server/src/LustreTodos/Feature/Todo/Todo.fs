namespace LustreTodos.Feature.Todo

open System

open LustreTodos.Shared.DomainError

/// <summary>A todo's title: trimmed, non-empty, and at most <c>MaxLength</c> characters. The case is
/// private, so <c>create</c> is the only way to make one.</summary>
type TodoTitle = private TodoTitle of string

module TodoTitle =
    /// <summary>Shared by the validation below and the request types' OpenAPI annotations.</summary>
    [<Literal>]
    let MaxLength = 256

    let private nonEmpty (title : string) =
        if String.IsNullOrWhiteSpace title then
            Error (ValidationFailed "Title cannot be null or just whitespace")
        else
            Ok title

    let private acceptableLength (title : string) =
        if title.Length > MaxLength then
            Error (
                ValidationFailed
                    $"Title is too long. Titles must be at most \
                    %i{MaxLength} characters, but got %i{title.Length}"
            )
        else
            Ok title

    /// <summary>Trim whitespace, then validate.</summary>
    let create (title : string) =
        title.Trim ()
        |> nonEmpty
        |> Result.bind acceptableLength
        |> Result.map TodoTitle

    let value (TodoTitle title) = title

    /// <summary>Skips validation, for values read back from the database, which were validated on
    /// write.</summary>
    let internal unsafeFromString title = TodoTitle title

type Todo = {
    Id : Guid
    Title : TodoTitle
    Completed : bool
    CreatedAt : DateTime
}
