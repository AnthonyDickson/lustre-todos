namespace LustreTodos.Shared.Coders

open System
open Thoth.Json.Net

module Extra =
    let epoch =
        let encoder (dt : DateTime) =
            let dto = DateTimeOffset dt
            dto.ToUnixTimeSeconds () |> float |> Encode.float

        let decoder (path : string) (value : JsonValue) =
            match Decode.int64 path value with
            | Ok v -> Ok v
            | Error _ -> Decode.float path value |> Result.map int64
            |> Result.map (fun s -> DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime)

        Extra.empty
        |> Extra.withCustom encoder decoder
        |> Extra.withDecimal
        |> Extra.withInt64

module Decode =
    let inline cachedDecoder<'T> : Decoder<'T> =
        Decode.Auto.generateDecoderCached<'T> (caseStrategy = CamelCase, extra = Extra.epoch)

    let inline fromString<'T> (json : string) : Result<'T, string> =
        Decode.fromString cachedDecoder<'T> json

module Encode =
    let inline cachedEncoder<'T> : Encoder<'T> =
        Encode.Auto.generateEncoderCached<'T> (caseStrategy = CamelCase, extra = Extra.epoch, skipNullField = false)

    let inline toString<'T> (value : 'T) : string =
        let jsonValue = cachedEncoder<'T> value
        Encode.toString 0 jsonValue
