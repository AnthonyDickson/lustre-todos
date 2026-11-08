namespace LustreTodos.Shared.Coders

open System
open System.Globalization
open System.Text.RegularExpressions
open Thoth.Json.Net

module Extra =
    /// <summary>Instants are RFC 3339 strings. See docs/dates.md.</summary>
    module Instant =
        /// <summary>Write in UTC with a <c>Z</c> offset and only the fraction
        /// digits needed, e.g. <c>2026-10-03T04:23:50.5Z</c>.</summary>
        let encode (instant : DateTimeOffset) =
            Encode.string (
                instant.UtcDateTime.ToString (
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                    CultureInfo.InvariantCulture
                )
            )

        /// <summary>RFC 3339 requires an offset. Without this check, a time
        /// with none would parse as the server's local time.</summary>
        let private rfc3339 =
            Regex (
                @"^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})$",
                RegexOptions.Compiled
            )

        /// <summary>Parse an RFC 3339 instant regardless of the server's
        /// culture.</summary>
        let tryParse (value : string) =
            if rfc3339.IsMatch value then
                match
                    DateTimeOffset.TryParse (
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None
                    )
                with
                | true, instant -> Some instant
                | false, _ -> None
            else
                None

        let decoder : Decoder<DateTimeOffset> =
            Decode.string
            |> Decode.andThen (fun value ->
                match tryParse value with
                | Some instant -> Decode.succeed instant
                | None ->
                    Decode.fail
                        $"Expected an RFC 3339 timestamp with an offset, got {value}")

    let extra =
        Extra.empty
        |> Extra.withDecimal
        |> Extra.withInt64
        |> Extra.withCustom Instant.encode Instant.decoder

module Decode =
    let inline cachedDecoder<'T> : Decoder<'T> =
        Decode.Auto.generateDecoderCached<'T> (
            caseStrategy = CamelCase,
            extra = Extra.extra
        )

    let inline fromString<'T> (json : string) : Result<'T, string> =
        Decode.fromString cachedDecoder<'T> json

module Encode =
    let inline cachedEncoder<'T> : Encoder<'T> =
        Encode.Auto.generateEncoderCached<'T> (
            caseStrategy = CamelCase,
            extra = Extra.extra,
            skipNullField = false
        )

    let inline toString<'T> (value : 'T) : string =
        let jsonValue = cachedEncoder<'T> value
        Encode.toString 0 jsonValue
