namespace LustreTodos.Tests.Auth

module ReturnUrlTests =
    open Expecto

    open LustreTodos.Feature.Auth

    /// The client's origin in development, which differs from the server's.
    let private baseUrl = "http://localhost:5173/"

    [<Tests>]
    let tests =
        testList "ReturnUrl" [
            testCase "a login returns to the page the user was on"
            <| fun () ->
                let page = "/active?sort=asc#x"

                Expect.equal
                    (ReturnUrl.resolve baseUrl (Some page))
                    ("http://localhost:5173" + page)
                    "the page is kept, on the client's origin"

            testCase "without a page, a login returns to the configured URL"
            <| fun () ->
                Expect.equal (ReturnUrl.resolve baseUrl None) baseUrl "no page"

                Expect.equal
                    (ReturnUrl.resolve baseUrl (Some ""))
                    baseUrl
                    "an empty page"

            testCase "a login never returns to another site"
            <| fun () ->
                for requested in
                    [
                        "https://evil.example.com/"
                        "//evil.example.com/"
                        "/\\evil.example.com/"
                        "javascript:alert(1)"
                        "active"
                        "/active\r\nSet-Cookie: x=1"
                    ] do
                    Expect.equal
                        (ReturnUrl.resolve baseUrl (Some requested))
                        baseUrl
                        $"{requested} falls back to the configured URL"
        ]
