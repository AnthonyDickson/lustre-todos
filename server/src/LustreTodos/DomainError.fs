namespace LustreTodos.DomainError

type DomainError =
    /// The client provided input that violates domain rules.
    /// This may include database constraint violations.
    | ValidationFailed of string
    /// The requested resource could not be found.
    | NotFound of string
    /// The "sub" claim could not be found in the OIDC token.
    | Unauthorised
    /// An unhandled database constraint violation. Constraint violations should
    /// be handled via helpers that explicitly check the constraint and return a
    /// `ValidationFailed` error. This error variant exists to make it clear
    /// where there are unhandled constraints.
    | Conflict of exn
    /// An unhandled database error.
    | DatabaseError of exn
    | UnhandledException of exn
