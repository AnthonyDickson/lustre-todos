namespace LustreTodos.Feature.Auth

/// <summary>Where a login returns to: the page the user was on, so an expired
/// session does not drop them on the home page.</summary>
[<RequireQualifiedAccess>]
module ReturnUrl =
    open System

    /// <summary>Whether <c>url</c> is a path on this site, as ASP.NET Core's
    /// <c>IsLocalUrl</c> decides: it starts with a single <c>/</c>, since
    /// <c>//host</c> and <c>/\host</c> name another host, and holds no control
    /// characters.</summary>
    let isLocalPath (url : string) =
        not (String.IsNullOrEmpty url)
        && url[0] = '/'
        && (url.Length = 1 || (url[1] <> '/' && url[1] <> '\\'))
        && not (Seq.exists Char.IsControl url)

    /// <summary>The URL to return to after logging in: <c>requested</c>, a
    /// path from the client, resolved against <c>baseUrl</c>, the configured
    /// return URL, which is absolute since the client may be served from
    /// another origin, as in development. Anything other than a local path
    /// falls back to <c>baseUrl</c>, so the login cannot be used to redirect
    /// to another site.</summary>
    let resolve (baseUrl : string) (requested : string option) : string =
        match requested with
        | Some path when isLocalPath path -> Uri(Uri baseUrl, path).AbsoluteUri
        | _ -> baseUrl
