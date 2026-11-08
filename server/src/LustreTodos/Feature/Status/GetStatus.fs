namespace LustreTodos.Feature.Status

open System
open System.Collections.Generic
open System.Reflection
open System.Threading
open System.Threading.Tasks

open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open Microsoft.OpenApi
open Oxpecker
open Oxpecker.OpenApi

open LustreTodos.Data.Db
open LustreTodos.Shared.Json
open LustreTodos.Shared.RequestLogging

/// <summary>Outcome of the database connectivity probe.</summary>
type DatabaseHealth = {
    /// <summary><c>healthy</c> when the probe succeeded, <c>unhealthy</c> otherwise.</summary>
    Status : string

    /// <summary>Details of the failure, present only when the probe failed.</summary>
    Error : string option
}

/// <summary>Payload returned by the public status endpoint.</summary>
type StatusResponse = {
    /// <summary>The version of the running build, taken from the assembly informational version.</summary>
    Version : string

    /// <summary>The ASP.NET Core hosting environment name (e.g. Development or Production).</summary>
    Environment : string

    /// <summary>UTC time the server process started.</summary>
    StartedAt : DateTime

    /// <summary>Seconds elapsed since the server process started.</summary>
    UptimeSeconds : int64

    /// <summary>Time since the server process started in a human-readable format.</summary>
    Uptime : string

    /// <summary>Outcome of the database connectivity probe.</summary>
    Database : DatabaseHealth
}

module GetStatus =
    [<Literal>]
    let Path = "/api/status"

    [<Literal>]
    let Healthy = "healthy"

    [<Literal>]
    let Unhealthy = "unhealthy"

    let private probeTimeout = TimeSpan.FromSeconds 2.0

    let private version () =
        match Assembly.GetEntryAssembly () with
        | null -> "unknown"
        | assembly ->
            match assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute> () with
            | null -> string (assembly.GetName().Version)
            | attr -> attr.InformationalVersion

    let formatUptime (uptime : TimeSpan) =
        let ts = uptime

        match ts.Days with
        | 0 -> sprintf "%dh %dm %ds" ts.Hours ts.Minutes ts.Seconds
        | d -> sprintf "%dd %dh %dm" d ts.Hours ts.Minutes

    let private checkDatabase (queryContext : QueryContextFactory) (ct : CancellationToken) =
        let probe =
            task {
                try
                    use! ctx = queryContext.OpenContextAsync ()

                    use cmd = ctx.Connection.CreateCommand ()
                    cmd.CommandText <- "SELECT 1"
                    let! _ = cmd.ExecuteScalarAsync ct

                    return { Status = Healthy; Error = None }, []
                with ex ->
                    let logEntry =
                        LogEntry.warn "Database health probe failed" [ LogProp.prop "exception" ex.Message ]

                    return
                        {
                            Status = Unhealthy
                            Error = Some "database unreachable"
                        },
                        [ logEntry ]
            }

        task {
            let timeout = Task.Delay probeTimeout
            let! winner = Task.WhenAny (probe, timeout)

            if winner = probe then
                return probe.Result
            else
                let logEntry = LogEntry.warn "Database health probe timed out" []

                return
                    {
                        Status = Unhealthy
                        Error = Some "database probe timed out"
                    },
                    [ logEntry ]
        }

    let private createProbeFactory (connectionString : string) =
        let csb = SqliteConnectionStringBuilder connectionString
        csb.DefaultTimeout <- 2
        QueryContextFactory.Create (csb.ToString ())

    let private handler
        (queryContext : QueryContextFactory)
        (environment : string)
        (startedAt : DateTime)
        : EndpointHandler =
        fun (ctx : HttpContext) ->
            task {
                use cts = CancellationTokenSource.CreateLinkedTokenSource ctx.RequestAborted
                cts.CancelAfter probeTimeout

                let! database, logEntries = checkDatabase queryContext cts.Token
                (RequestLog.fromContext ctx).AddMany logEntries

                let uptime = DateTime.UtcNow - startedAt

                let response = {
                    Version = version ()
                    Environment = environment
                    StartedAt = startedAt
                    UptimeSeconds = int64 uptime.TotalSeconds
                    Uptime = formatUptime uptime
                    Database = database
                }

                if database.Status <> Healthy then
                    ctx.SetStatusCode 503

                do! Json.write ctx response
            }

    /// <summary>The status endpoint, with its own short-timeout connection so a slow database cannot stall
    /// the probe.</summary>
    let endpoint (connectionString : string) (environment : string) (startedAt : DateTime) =
        route Path (handler (createProbeFactory connectionString) environment startedAt)
        |> addOpenApi (
            OpenApiConfig (
                responseBodies = [|
                    ResponseBody typeof<StatusResponse>
                    ResponseBody (typeof<StatusResponse>, statusCode = 503)
                |],
                configureOperation =
                    fun op _ _ ->
                        op.Summary <- "Service status"

                        op.Description <-
                            "Reports the deployed version, uptime, and database health. \
                            Returns 503 when the database is unreachable."

                        op.Tags <- HashSet [ OpenApiTagReference "Status" ]
                        Task.CompletedTask
            )
        )
