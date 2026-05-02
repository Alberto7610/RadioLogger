-- Tabla para almacenar API Keys de equipos emparejados
-- Ejecutar en RadioLoggerDB (100.75.6.95)

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApiKeys')
BEGIN
    CREATE TABLE ApiKeys (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        [Key] NVARCHAR(64) NOT NULL,
        MachineId NVARCHAR(100) NOT NULL,
        MachineName NVARCHAR(200) NOT NULL DEFAULT '',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsActive BIT NOT NULL DEFAULT 1
    );

    CREATE UNIQUE INDEX IX_ApiKeys_Key ON ApiKeys ([Key]);
    CREATE INDEX IX_ApiKeys_MachineId ON ApiKeys (MachineId);

    PRINT 'Tabla ApiKeys creada exitosamente.';
END
ELSE
BEGIN
    PRINT 'Tabla ApiKeys ya existe.';
END
