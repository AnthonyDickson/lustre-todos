namespace LustreTodos.Constraints

/// <summary>A failed constraint check. Constraint checks mirror the database's integrity constraints but
/// produce a friendly, actionable error message. This type is deliberately narrower than
/// <c>DomainError</c>: it can only represent validation failures, so combining multiple failures can
/// never drop or misclassify an error. Constraint errors are mapped to
/// <c>DomainError.ValidationFailed</c> by <c>requireAll</c> and <c>requireOne</c>.</summary>
type ConstraintError = ConstraintError of string

/// <summary>Explicit checks for the database's integrity constraints. These checks exist to provide
/// meaningful error messages, since SQLite does not always report which column triggered a constraint
/// violation. Always run checks through <c>requireAll</c> (or <c>requireOne</c> for a single check) so
/// failures are mapped to a <c>DomainError</c>.</summary>
[<RequireQualifiedAccess>]
module Constraints =
    open System.Threading.Tasks

    open FsToolkit.ErrorHandling

    open LustreTodos.DomainError

    /// <summary>Combine a list of failed checks into a single error, joining their messages. The input
    /// is guaranteed to be non-empty: it comes from a failed <c>List.sequenceTaskResultA</c>, which
    /// fails with at least one error.</summary>
    let private combineValidationErrors =
        function
        | [] -> invalidOp "combineValidationErrors requires at least one error"
        | errors ->
            errors
            |> List.map (fun (ConstraintError error) -> error)
            |> String.concat "; "
            |> ConstraintError

    /// <summary>Run all constraint checks, collecting every failure instead of stopping at the first.
    /// Checks run concurrently (applicative) and their messages are joined into a single
    /// <c>ValidationFailed</c> error, so a client sees all violations in one response.</summary>
    let requireAll (constraints : Task<Result<unit, ConstraintError>> list) : Task<Result<unit, DomainError>> =
        task {
            match! List.sequenceTaskResultA constraints with
            | Ok _ -> return Ok ()
            | Error errors ->
                let (ConstraintError error) = combineValidationErrors errors
                return Error (ValidationFailed error)
        }

    /// <summary>Run a single constraint check, mapping any failure to a <c>ValidationFailed</c>
    /// error.</summary>
    let requireOne (constraint_ : Task<Result<unit, ConstraintError>>) : Task<Result<unit, DomainError>> =
        requireAll [ constraint_ ]
