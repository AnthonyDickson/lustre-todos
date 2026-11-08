namespace LustreTodos.Feature.TestSupport

/// Dev-only endpoint used by the E2E suite to clear all of the current user's
/// data before each test (and retry). It is registered only in the Development
/// environment (see `Program.fs`), so it cannot be reached against real data.
module ResetUserData =
    open System.Threading.Tasks

    open FsToolkit.ErrorHandling
    open Oxpecker

    open LustreTodos.Data.Db
    open LustreTodos.Shared.Auth
    open LustreTodos.Shared.Endpoint
    open LustreTodos.Shared.RequestLogging

    [<Literal>]
    let Path = "/api/test/user-data"

    /// Every table with a `UserId` column. The tables are discovered from the
    /// schema so a new table is reset without editing this module. Every
    /// foreign key cascades or sets null, so the delete order does not matter.
    let private userTablesSql =
        "SELECT m.name FROM sqlite_master m JOIN pragma_table_info(m.name) c "
        + "WHERE m.type = 'table' AND c.name = 'UserId'"

    let private deleteAll (queryContext : QueryContextFactory) (userId : string) : Task<unit> =
        task {
            use! ctx = queryContext.OpenContextAsync ()
            let conn = ctx.Connection
            use! tx = conn.BeginTransactionAsync ()

            let tables = ResizeArray<string> ()

            do!
                task {
                    use cmd = conn.CreateCommand ()
                    cmd.Transaction <- tx
                    cmd.CommandText <- userTablesSql
                    use! reader = cmd.ExecuteReaderAsync ()

                    while! reader.ReadAsync () do
                        tables.Add (reader.GetString 0)
                }

            for table in tables do
                use cmd = conn.CreateCommand ()
                cmd.Transaction <- tx
                // The table name comes from the schema, not the request.
                cmd.CommandText <- $"DELETE FROM \"{table}\" WHERE UserId = $userId"

                let param = cmd.CreateParameter ()
                param.ParameterName <- "$userId"
                param.Value <- userId
                cmd.Parameters.Add param |> ignore
                let! _ = cmd.ExecuteNonQueryAsync ()
                ()

            do! tx.CommitAsync ()
        }

    let private handler (queryContext : QueryContextFactory) : EndpointHandler =
        Endpoint.handler (fun ctx ->
            taskResult {
                let log = RequestLog.fromContext ctx
                let! userId = Auth.getUserId ctx
                do! deleteAll queryContext userId
                log.Info "Reset all data for test user"
                ctx.SetStatusCode 204
                return ()
            })

    let endpoint (queryContext : QueryContextFactory) = route Path (handler queryContext)
