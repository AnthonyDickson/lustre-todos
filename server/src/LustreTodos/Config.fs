namespace LustreTodos.Config

open System.ComponentModel.DataAnnotations

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options

[<CLIMutable>]
type OidcConfig = {
    [<Required>]
    Authority : string

    [<Required>]
    ClientId : string

    [<Required>]
    ClientSecret : string

    [<Required>]
    CallbackPath : string

    ValidAudiences : string array
}

[<CLIMutable>]
type OAuth2Config = {
    [<Required>]
    ClientId : string

    [<Required>]
    AuthorizationUrl : string

    [<Required>]
    TokenUrl : string
}

[<CLIMutable>]
type LoginConfig = {
    [<Required>]
    ReturnUrl : string
}

[<CLIMutable>]
type LoggingConfig = { FilePath : string }

/// <summary>Where ASP.NET Core Data Protection keeps the keys that encrypt the auth cookie. Without it
/// the keys live in the user profile, which a container loses when it is recreated, signing every user
/// out.</summary>
[<CLIMutable>]
type DataProtectionConfig = {
    [<Required>]
    KeysPath : string
}

type AppConfig = {
    ConnectionString : string
    Oidc : OidcConfig
    Oauth2 : OAuth2Config option
    Login : LoginConfig
    Logging : LoggingConfig
    DataProtection : DataProtectionConfig option
}

module Config =
    [<Literal>]
    let private ConnectionName = "Default"

    [<Literal>]
    let private OidcSectionName = "Oidc"

    [<Literal>]
    let private Oauth2SectionName = "OAuth2"

    [<Literal>]
    let private LoginSectionName = "Login"

    [<Literal>]
    let private LoggingSectionName = "Logging"

    [<Literal>]
    let private DataProtectionSectionName = "DataProtection"

    let private register<'T when 'T : not struct and 'T : (new : unit -> 'T)>
        (services : IServiceCollection)
        (config : IConfiguration)
        (sectionName : string)
        =
        services.AddOptions<'T>().Bind(config.GetSection sectionName).ValidateDataAnnotations().ValidateOnStart ()
        |> ignore

    /// Raises `OptionsValidationException` for missing or invalid config entries.
    let private read<'T when 'T : not struct and 'T : (new : unit -> 'T)>
        (config : IConfiguration)
        (sectionName : string)
        : 'T =
        let value = config.GetSection(sectionName).Get<'T> ()
        let validator = DataAnnotationValidateOptions<'T> Options.DefaultName
        let result = validator.Validate (Options.DefaultName, value)

        if result.Succeeded then
            value
        else
            raise (OptionsValidationException (sectionName, typeof<'T>, result.Failures))

    let private sectionExists (config : IConfiguration) (name : string) = (config.GetSection name).Exists ()

    /// Raises `OptionsValidationException` for missing or invalid config entries.
    let load (services : IServiceCollection) (config : IConfiguration) : AppConfig =
        register<OidcConfig> services config OidcSectionName
        register<LoginConfig> services config LoginSectionName
        register<LoggingConfig> services config LoggingSectionName

        let oauthOptions =
            if sectionExists config Oauth2SectionName then
                register<OAuth2Config> services config Oauth2SectionName
                Some (read config Oauth2SectionName)
            else
                None

        {
            ConnectionString = config.GetConnectionString ConnectionName
            Oidc = read config OidcSectionName
            Oauth2 = oauthOptions
            Login = read config LoginSectionName
            Logging = read config LoggingSectionName
            DataProtection =
                if sectionExists config DataProtectionSectionName then
                    Some (read config DataProtectionSectionName)
                else
                    None
        }
