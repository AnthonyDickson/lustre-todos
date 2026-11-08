namespace LustreTodos.Shared.OpenApi

open System

/// <summary>
/// Declarative hints describing how a refined or constrained value is represented on the wire.
/// A hint is applied to an OpenAPI schema by <see cref="OpenApi.SchemaHintTransformer"/>; the
/// annotated field stays a primitive, so decoding is unaffected.
/// </summary>
/// <remarks>
/// <para>
/// Prefer the native <c>System.ComponentModel.DataAnnotations</c> attributes wherever they express
/// the constraint: ASP.NET Core maps them onto JSON Schema keywords for free. Avoid
/// <c>[&lt;Required&gt;]</c> on F# records - see <see cref="OpenApi.FSharpRecordSchemaTransformer"/>.
/// </para>
/// <para>
/// Use <see cref="SchemaHint.EnumAttribute"/> to document a string enum derived from an F# union's
/// cases, and <see cref="SchemaHint.NumberAttribute"/> for a minimum-only boundary or
/// <c>multipleOf</c> that <c>[&lt;Range&gt;]</c> cannot express. Hints document only; they do not
/// validate, so keep them in step with the domain invariants.
/// </para>
/// <para>
/// See <c>docs/openapi.md</c> for the native attribute reference, the full guidance, and future
/// migrations (e.g. the F# union transformer in Oxpecker.OpenApi PR #98) and alternatives.
/// </para>
/// </remarks>
module SchemaHint =
    /// <summary>
    /// Documents a string property as an enum, deriving the accepted values from the cases of an
    /// F# discriminated union, e.g. <c>SchemaHint.Enum(typeof&lt;MyUnion&gt;)</c>.
    /// </summary>
    /// <remarks>
    /// The emitted values are the union case names, so they must match the wire strings. If a case
    /// name ever diverges from its wire value, either reflect a dedicated union whose names match or
    /// fall back to <c>[&lt;RegularExpression&gt;]</c>.
    /// </remarks>
    [<AttributeUsage(AttributeTargets.Class
                     ||| AttributeTargets.Struct
                     ||| AttributeTargets.Property
                     ||| AttributeTargets.Field)>]
    type EnumAttribute (unionType : Type) =
        inherit Attribute ()

        member _.UnionType = unionType

    /// <summary>
    /// Constrains a numeric property, e.g. a positive magnitude with
    /// <c>SchemaHint.Number (Minimum = "0", MultipleOf = 0.01)</c>.
    /// </summary>
    /// <remarks>
    /// Use this only where <c>[&lt;Range(min, max)&gt;]</c> is not enough: it requires both bounds,
    /// so it cannot express a minimum-only constraint on a positive magnitude. Bounds are JSON
    /// Schema keywords carried as strings because <c>OpenApiSchema.Minimum</c> and
    /// <c>OpenApiSchema.Maximum</c> are strings; leave a bound blank to omit it.
    /// </remarks>
    [<AttributeUsage(AttributeTargets.Class
                     ||| AttributeTargets.Struct
                     ||| AttributeTargets.Property
                     ||| AttributeTargets.Field)>]
    type NumberAttribute () =
        inherit Attribute ()

        member val Minimum = "" with get, set
        member val Maximum = "" with get, set
        member val MultipleOf = 0.0 with get, set

module OpenApi =
    open Microsoft.AspNetCore.OpenApi
    open Microsoft.FSharp.Reflection
    open Microsoft.OpenApi
    open System.Collections.Concurrent
    open System.Collections.Generic
    open System.IO
    open System.Reflection
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Threading
    open System.Threading.Tasks
    open System.Xml.Linq

    let private isOptionType (t : System.Type) : bool =
        if t.IsGenericType then
            let definition = t.GetGenericTypeDefinition ()
            definition = typedefof<option<_>> || definition = typedefof<voption<_>>
        else
            false

    /// <summary>
    /// Marks non-option record fields as required and fixes string property type inference in OpenAPI schemas.
    /// </summary>
    type FSharpRecordSchemaTransformer () =
        interface IOpenApiSchemaTransformer with
            member _.TransformAsync (schema, context, _cancellationToken : CancellationToken) =
                let jsonType = context.JsonTypeInfo.Type

                if FSharpType.IsRecord jsonType then
                    let required =
                        jsonType
                        |> FSharpType.GetRecordFields
                        |> Seq.filter (fun field -> not (isOptionType field.PropertyType))
                        |> Seq.map (fun field -> field.Name)
                        |> HashSet<string>

                    if required.Count > 0 then
                        schema.Required <- required

                if
                    not (isNull context.JsonPropertyInfo)
                    && context.JsonPropertyInfo.PropertyType = typeof<string>
                then
                    schema.Type <- JsonSchemaType.String

                    if not (isNull schema.OneOf) then
                        schema.OneOf.Clear ()

                    if not (isNull schema.AnyOf) then
                        schema.AnyOf.Clear ()

                    if not (isNull schema.AllOf) then
                        schema.AllOf.Clear ()

                Task.CompletedTask

    /// <summary>
    /// Populates schema and property descriptions from F# XML doc comments (`summary` tags on types and record fields).
    /// </summary>
    type XmlDocSchemaTransformer () =
        let loadedDocs = ConcurrentDictionary<string, Map<string, string>> ()

        let tryLoadDoc (asm : Assembly) : Map<string, string> =
            let xmlPath = Path.ChangeExtension (asm.Location, ".xml")

            if File.Exists xmlPath then
                let doc = XDocument.Load xmlPath

                doc.Descendants (XName.Get "member")
                |> Seq.choose (fun el ->
                    let name = el.Attribute (XName.Get "name") |> Option.ofObj

                    let summary =
                        el.Element (XName.Get "summary")
                        |> Option.ofObj
                        |> Option.map (fun e -> e.Value.Trim ())

                    match name, summary with
                    | Some n, Some s -> Some (n.Value, s)
                    | _ -> None)
                |> Map.ofSeq
            else
                Map.empty

        let getSummaries (asm : Assembly) : Map<string, string> =
            loadedDocs.GetOrAdd (asm.Location, fun _ -> tryLoadDoc asm)

        interface IOpenApiSchemaTransformer with
            member _.TransformAsync (schema, context, _cancellationToken : CancellationToken) =
                let jsonType = context.JsonTypeInfo.Type
                // .NET reflection uses '+' for nested types but XML doc uses '.'
                let xmlTypeName = jsonType.FullName.Replace ('+', '.')

                let summaries = getSummaries jsonType.Assembly

                // Set type-level description from <summary> on the type itself
                let typeKey = $"T:%s{xmlTypeName}"

                match summaries.TryFind typeKey with
                | Some summary -> schema.Description <- summary
                | None -> ()

                // Set field-level descriptions from <summary> on record fields
                if FSharpType.IsRecord jsonType then
                    for field in FSharpType.GetRecordFields jsonType do
                        let fieldKey = $"P:%s{xmlTypeName}.%s{field.Name}"
                        let jsonName = JsonNamingPolicy.CamelCase.ConvertName field.Name

                        match summaries.TryFind fieldKey with
                        | Some summary ->
                            if not (isNull schema.Properties) && schema.Properties.ContainsKey jsonName then
                                schema.Properties[jsonName].Description <- summary
                        | None -> ()

                Task.CompletedTask

    /// <summary>
    /// Applies <see cref="SchemaHint"/> attributes to generated schemas, so refined values are
    /// documented as their wire representation: an enum derived from a union's cases, or a
    /// constrained number.
    /// </summary>
    type SchemaHintTransformer () =
        interface IOpenApiSchemaTransformer with
            member _.TransformAsync (schema, context, _cancellationToken : CancellationToken) =
                let apply (provider : ICustomAttributeProvider) =
                    for attribute in provider.GetCustomAttributes (typeof<SchemaHint.EnumAttribute>, false) do
                        let hint = attribute :?> SchemaHint.EnumAttribute

                        schema.Type <- Nullable JsonSchemaType.String

                        schema.Enum <-
                            ResizeArray<JsonNode> [
                                for case in FSharpType.GetUnionCases hint.UnionType ->
                                    JsonValue.Create case.Name :> JsonNode
                            ]

                    for attribute in provider.GetCustomAttributes (typeof<SchemaHint.NumberAttribute>, false) do
                        let hint = attribute :?> SchemaHint.NumberAttribute

                        schema.Type <- Nullable JsonSchemaType.Number
                        schema.Format <- null
                        schema.Pattern <- null

                        if not (String.IsNullOrEmpty hint.Minimum) then
                            schema.Minimum <- hint.Minimum

                        if not (String.IsNullOrEmpty hint.Maximum) then
                            schema.Maximum <- hint.Maximum

                        if hint.MultipleOf > 0.0 then
                            schema.MultipleOf <- Nullable (decimal hint.MultipleOf)

                if not (isNull context.JsonPropertyInfo) then
                    apply context.JsonPropertyInfo.AttributeProvider

                apply context.JsonTypeInfo.Type

                Task.CompletedTask

    /// <summary>
    /// Documents <c>decimal</c> as a plain JSON number. System.Text.Json otherwise infers a
    /// <c>number</c>-or-<c>string</c> union with a <c>double</c> format and a numeric-string pattern.
    /// </summary>
    type DecimalSchemaTransformer () =
        interface IOpenApiSchemaTransformer with
            member _.TransformAsync (schema, context, _cancellationToken : CancellationToken) =
                if context.JsonTypeInfo.Type = typeof<decimal> then
                    schema.Type <- Nullable JsonSchemaType.Number
                    schema.Format <- null
                    schema.Pattern <- null

                Task.CompletedTask
