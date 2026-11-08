# Dates and Timestamps

Two kinds of time value, never mixed:

|         | Calendar date                 | Instant                                           |
| ------- | ----------------------------- | ------------------------------------------------- |
| Use for | A day the user picks          | An event at a point in time                       |
| Example | A due date                    | When a todo was created                           |
| SQLite  | `DATE`                        | `DATETIME`, UTC                                   |
| F#      | `DateOnly`                    | `DateTimeOffset` in code, UTC `DateTime` in rows  |
| Gleam   | `calendar.Date`               | `Timestamp`                                       |
| JSON    | ISO 8601 date, `"2026-03-08"` | RFC 3339 with an offset, `"2026-10-03T04:23:50Z"` |

A calendar date is not converted to an instant: a day has no time of day, and
any chosen time (e.g. local midnight) is a different day in UTC and shifts if
the timezone used to read it changes. The template has no calendar dates yet;
the first one needs a `DateOnly` JSON codec that accepts only `yyyy-MM-dd`,
independent of culture.

## Why RFC 3339 rather than Unix timestamps

- It is OpenAPI's standard `date-time` format, so the schema needs no custom
  transformer and generated clients get native date types.
- It is readable in API responses, logs, and the `sqlite3` shell.
- It has no fixed precision and no seconds-or-milliseconds ambiguity.
- It pairs with ISO 8601 calendar dates, so both kinds of time value use one
  family of formats.

The cost is that UTC is a rule rather than a property of the type, which the
sections below enforce in one place each.

## Instants

`DATETIME` columns carry no offset and are UTC. A column that deviates has to
say so in its migration comment.

- **Writing.** `Data/Instant.fs` converts to UTC before the value reaches the
  column, so the stored value is UTC whatever offset the caller held. SQL writes
  (triggers, inserts in SQL) are outside the type system and apply the same rule
  by hand; SQLite's `datetime('now')` is already UTC.
- **Reading.** SQLite returns the value without a kind, so `Data/Instant.fs`
  marks it as UTC. Codecs use it rather than converting themselves.
- **Ordering.** Range queries and `ORDER BY` compare the stored text, which
  orders correctly only while every write goes through the same conversion.
- **JSON.** The server writes UTC with a `Z` offset and accepts any offset, but
  rejects a time without one, which would otherwise be read as the server's
  local time. See `Extra.Instant` in `Shared/Coders.fs`.
- **Display.** Instants are converted to local time on the client, only for
  display.

## Not yet decided

A server-side timezone is needed only once the server works out days or periods
without a client request (e.g. a scheduled job). Until then the server needs no
timezone configuration: the client works out any date range from its local
calendar and sends it.
