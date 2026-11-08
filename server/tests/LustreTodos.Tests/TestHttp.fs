namespace LustreTodos.Tests

open System.Net.Http
open System.Text
open Expecto

open LustreTodos.Shared.Coders

/// JSON helpers for driving the test HTTP client.
module TestHttp =
    let private jsonContent (value : 'T) =
        new StringContent (
            Encode.toString value,
            Encoding.UTF8,
            "application/json"
        )

    let postJson (client : HttpClient) (url : string) (value : 'T) =
        client.PostAsync (url, jsonContent value)

    let patchJson (client : HttpClient) (url : string) (value : 'T) =
        client.PatchAsync (url, jsonContent value)

    /// Fill the Oxpecker routef `{%O:guid}` placeholder in an item path with a
    /// concrete id.
    let itemPath (path : string) (id : System.Guid) =
        path.Replace ("{%O:guid}", id.ToString ())

    /// Decode a JSON response body, failing the test with the decoder's message
    /// rather than an opaque null.
    let readJson<'T> (response : HttpResponseMessage) : Async<'T> =
        async {
            let! body = response.Content.ReadAsStringAsync () |> Async.AwaitTask

            match Decode.fromString<'T> body with
            | Ok value -> return value
            | Error error -> return failtest error
        }
