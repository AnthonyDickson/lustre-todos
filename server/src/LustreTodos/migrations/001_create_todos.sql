-- Note that while `GUID`, `BOOLEAN` and `DATETIME` are not real SQLite data types,
-- they act as hints to SqlHydra for codegen, see
-- https://github.com/JordanMarr/SqlHydra/blob/main/src/SqlHydra.Cli/Sqlite/SqliteDataTypes.fs
-- for the full list of supported types.
CREATE TABLE [Todos] (
    -- Expected to be v7 UUIDs for better sorting and performance
    Id        GUID NOT NULL PRIMARY KEY,
    -- User IDs are likely GUIDs, but we play it safe by not making any assumptions
    -- since the identity provider chooses the format.
    UserId    TEXT NOT NULL,
    Title     TEXT NOT NULL,
    Completed BOOLEAN NOT NULL DEFAULT 0,
    -- UTC, see docs/dates.md
    CreatedAt DATETIME NOT NULL
);

CREATE INDEX IX_Todos_UserId on [Todos](UserId);
CREATE INDEX IX_Todos_UserId_Completed on [Todos](UserId, Completed);
