#!/usr/bin/env -S dotnet fsi

#r "nuget: dbup-sqlite, 6.0.4"
#r "nuget: Microsoft.Data.Sqlite, 10.0.9"

open System
open System.Collections.Generic
open System.IO
open DbUp
open DbUp.Engine
open Microsoft.Data.Sqlite

let connectionString = "Data Source=src/LustreTodos/app.sqlite3"
let migrationsPath = "src/LustreTodos/migrations"
let scriptPrefix = "LustreTodos.migrations."

// DbUp's WithScriptsFromFileSystem uses bare filenames as ScriptName,
// but WithScriptsEmbeddedInAssembly uses the full resource path
// (e.g. LustreTodos.migrations.001_create_todos.sql).
// Manually loading scripts with the namespace prefix ensures the
// SchemaVersions journal is consistent regardless of how migrations are run.
let scripts =
    Directory.GetFiles(migrationsPath, "*.sql")
    |> Array.sort
    |> Array.map (fun path ->
        let name = scriptPrefix + Path.GetFileName path
        SqlScript(name, File.ReadAllText path))
    |> fun xs -> xs :> IEnumerable<SqlScript>

let result =
    DeployChanges.To
        .SqliteDatabase(connectionString)
        .WithScripts(scripts)
        // A script that fails partway leaves the schema as it was, as in the server.
        .WithTransactionPerScript()
        .LogToConsole()
        .Build()
        .PerformUpgrade()

if not result.Successful then
    failwithf "Database migration failed: %O" result.Error

using (new SqliteConnection(connectionString)) (fun conn ->
    conn.Open()

    using (conn.CreateCommand()) (fun walCmd ->
        walCmd.CommandText <- "PRAGMA journal_mode = WAL"
        walCmd.ExecuteNonQuery() |> ignore)

    using (conn.CreateCommand()) (fun fkCmd ->
        fkCmd.CommandText <- "PRAGMA foreign_keys = ON"
        fkCmd.ExecuteNonQuery() |> ignore))

printfn "Migrations complete."
