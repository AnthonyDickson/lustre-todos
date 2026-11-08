namespace LustreTodos.Shared.Logging

/// <summary>The global Serilog pipeline: startup, framework, and unhandled
/// exception logs. Request-scoped logs are in <c>RequestLogging</c>.</summary>
[<RequireQualifiedAccess>]
module Logging =
    open System

    open Microsoft.Extensions.Hosting
    open Serilog
    open Serilog.Events
    open Serilog.Formatting.Compact

    open LustreTodos.Shared.Config

    /// <summary>Log compact JSON to the console, and to a file when one is
    /// configured. <c>statusPath</c> is the health check endpoint, whose
    /// successful probes are dropped.</summary>
    let configure
        (statusPath : string)
        (loggingOptions : LoggingConfig)
        (_ : HostBuilderContext)
        (config : LoggerConfiguration)
        =
        let filePath =
            loggingOptions.FilePath
            |> Option.ofObj
            |> Option.filter (not << String.IsNullOrWhiteSpace)

        // The app's request log records every request, so the framework's own
        // per-request logs (routing, static files, hosting diagnostics) only
        // duplicate it. Warnings and errors still come through.
        config.MinimumLevel
            .Information()
            .MinimumLevel.Override (
                "Microsoft.AspNetCore",
                LogEventLevel.Warning
            )
        |> ignore

        // Filter out healthcheck noise for the status endpoint. Framework
        // per-request logs (Microsoft.AspNetCore.Hosting.Diagnostics) carry no
        // status code, so they are dropped whenever the path matches. The app's
        // own buffered request-log summary does carry the status code, so only
        // successful probes are dropped; failures (503) stay visible.
        config.Filter.ByExcluding (fun (e : LogEvent) ->
            let prop name =
                match e.Properties.TryGetValue name with
                | true, (:? ScalarValue as sv) -> string sv.Value
                | _ -> ""

            let source = prop "SourceContext"
            let requestPath = prop "RequestPath"

            let statusCode =
                match e.Properties.TryGetValue "StatusCode" with
                | true, (:? ScalarValue as sv) ->
                    match Int32.TryParse (string sv.Value) with
                    | true, code -> code
                    | _ -> 0
                | _ -> 0

            let isStatusProbe = requestPath.StartsWith statusPath

            let isFrameworkLog =
                source.StartsWith "Microsoft.AspNetCore"
                || source.StartsWith "Microsoft.Hosting"

            isStatusProbe && (isFrameworkLog || statusCode < 400))
        |> ignore

        config.WriteTo.Console (RenderedCompactJsonFormatter ()) |> ignore

        match filePath with
        | Some path ->
            // Roll at 10 MB and keep 10 files, bounding the log at about
            // 100 MB. Without rolling, the sink stops writing once the file
            // reaches its 1 GB default limit.
            config.WriteTo.File (
                RenderedCompactJsonFormatter (),
                path,
                fileSizeLimitBytes = 10L * 1024L * 1024L,
                rollOnFileSizeLimit = true,
                retainedFileCountLimit = 10
            )
            |> ignore
        | None -> ()
