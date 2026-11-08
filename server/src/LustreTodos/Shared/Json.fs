namespace LustreTodos.Shared.Json

open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open LustreTodos.Shared.DomainError
open LustreTodos.Shared.Coders

/// <summary>HTTP helpers for reading and writing JSON payloads.</summary>
module Json =
    let write (ctx : HttpContext) (object : 'T) =
        task {
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            return! ctx.Response.WriteAsync (Encode.toString object)
        }

    let read (ctx : HttpContext) =
        task {
            use reader = new StreamReader (ctx.Request.Body, Encoding.UTF8)
            let! body = reader.ReadToEndAsync ()
            return Decode.fromString body |> Result.mapError ValidationFailed
        }
