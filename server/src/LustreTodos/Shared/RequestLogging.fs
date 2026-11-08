namespace LustreTodos.Shared.RequestLogging

open System
open Microsoft.AspNetCore.Http

type LogProperty = { Key : string; Value : obj }

[<RequireQualifiedAccess>]
module LogProp =
    let prop (key : string) (value : 'T) = { Key = key; Value = box value }

[<RequireQualifiedAccess>]
type LogLevel =
    | Info
    | Warning
    | Error

[<RequireQualifiedAccess>]
module LogLevel =
    let toString =
        function
        | LogLevel.Info -> "Info"
        | LogLevel.Warning -> "Warning"
        | LogLevel.Error -> "Error"

type LogEntry = {
    Level : LogLevel
    Message : string
    Properties : LogProperty list
    Timestamp : DateTimeOffset
}

[<RequireQualifiedAccess>]
module LogEntry =
    let info msg props = {
        Level = LogLevel.Info
        Message = msg
        Properties = props
        Timestamp = DateTimeOffset.UtcNow
    }

    let warn msg props = {
        Level = LogLevel.Warning
        Message = msg
        Properties = props
        Timestamp = DateTimeOffset.UtcNow
    }

    let error msg props = {
        Level = LogLevel.Error
        Message = msg
        Properties = props
        Timestamp = DateTimeOffset.UtcNow
    }

type RequestLog () =
    let entries = ResizeArray<LogEntry> ()

    member _.Info (msg : string, [<ParamArray>] props : LogProperty[]) =
        entries.Add {
            Level = LogLevel.Info
            Message = msg
            Properties = List.ofArray props
            Timestamp = DateTimeOffset.UtcNow
        }

    member this.Info (msg : string) = this.Info (msg, [||])

    member _.Warn (msg : string, [<ParamArray>] props : LogProperty[]) =
        entries.Add {
            Level = LogLevel.Warning
            Message = msg
            Properties = List.ofArray props
            Timestamp = DateTimeOffset.UtcNow
        }

    member this.Warn (msg : string) = this.Warn (msg, [||])

    member _.Error (msg : string, [<ParamArray>] props : LogProperty[]) =
        entries.Add {
            Level = LogLevel.Error
            Message = msg
            Properties = List.ofArray props
            Timestamp = DateTimeOffset.UtcNow
        }

    member this.Error (msg : string) = this.Error (msg, [||])

    member _.AddMany msgs = entries.AddRange msgs
    member _.Entries = entries |> List.ofSeq


module RequestLog =
    [<Literal>]
    let Key = "RequestLog"

    let fromContext (ctx : HttpContext) =
        match ctx.Items.TryGetValue Key with
        | true, (:? RequestLog as log) -> log
        | _ -> failwith "RequestLog not found in HttpContext.Items — is the RequestLogging middleware wired?"

module Middleware =
    open System.Collections.Generic
    open System.Diagnostics
    open System.Threading.Tasks

    open Microsoft.AspNetCore.Authentication
    open Serilog
    open Serilog.Context

    let private entryToDict (e : LogEntry) =
        let dict = Dictionary<string, obj> ()
        dict["level"] <- LogLevel.toString e.Level
        dict["message"] <- e.Message
        dict["timestamp"] <- box e.Timestamp

        for prop in e.Properties do
            dict[prop.Key] <- prop.Value

        dict

    let requestLogging (logger : ILogger) (next : RequestDelegate) : RequestDelegate =
        RequestDelegate (fun (ctx : HttpContext) ->
            task {
                let sw = Stopwatch.StartNew ()
                ctx.Items[RequestLog.Key] <- RequestLog ()

                try
                    return! next.Invoke ctx
                finally
                    let user =
                        ctx.User.FindFirst "sub"
                        |> Option.ofObj
                        |> Option.map _.Value
                        |> Option.defaultValue null

                    let log = RequestLog.fromContext ctx
                    let entries = log.Entries

                    let maxLevel =
                        entries
                        |> List.fold
                            (fun acc e ->
                                match acc, e.Level with
                                | LogLevel.Error, _
                                | _, LogLevel.Error -> LogLevel.Error
                                | LogLevel.Warning, _
                                | _, LogLevel.Warning -> LogLevel.Warning
                                | _ -> LogLevel.Info)
                            LogLevel.Info

                    let logArray = entries |> List.map (entryToDict >> box) |> List.toArray

                    let serilogLevel =
                        match maxLevel with
                        | LogLevel.Error -> Serilog.Events.LogEventLevel.Error
                        | LogLevel.Warning -> Serilog.Events.LogEventLevel.Warning
                        | LogLevel.Info -> Serilog.Events.LogEventLevel.Information

                    logger.Write (
                        serilogLevel,
                        "{Method} {RequestPath} {StatusCode} {ElapsedMs} {RequestId} {UserId} {@Log}",
                        ctx.Request.Method,
                        ctx.Request.Path.Value,
                        ctx.Response.StatusCode,
                        sw.ElapsedMilliseconds,
                        ctx.TraceIdentifier,
                        user,
                        logArray
                    )
            }
            :> Task)
