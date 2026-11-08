namespace LustreTodos.Data

open System

/// <summary>Maps instants to and from <c>DATETIME</c> columns, which hold UTC with no offset. Codecs
/// use these rather than converting themselves, so the rule is written once. See
/// docs/dates.md.</summary>
module Instant =
    /// <summary>Convert to UTC, whatever offset the caller held.</summary>
    let toColumn (instant : DateTimeOffset) : DateTime = instant.UtcDateTime

    /// <summary>SQLite returns the value with an unspecified kind; mark it as the UTC it was stored
    /// as.</summary>
    let fromColumn (value : DateTime) : DateTimeOffset =
        DateTimeOffset (DateTime.SpecifyKind (value, DateTimeKind.Utc))
