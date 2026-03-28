-- Tablas de usuarios y auditoría para RadioLogger Dashboard

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AppUsers')
BEGIN
    CREATE TABLE AppUsers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        PasswordHash NVARCHAR(MAX) NOT NULL,
        DisplayName NVARCHAR(MAX) NOT NULL DEFAULT '',
        [Role] NVARCHAR(20) NOT NULL DEFAULT 'Operador',
        IsActive BIT NOT NULL DEFAULT 1,
        TelegramChatId NVARCHAR(100) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastLogin DATETIME2 NULL
    );
    CREATE UNIQUE INDEX IX_AppUsers_Username ON AppUsers (Username);

    -- Admin seed: admin / Admin123!
    -- Hash SHA256 de "Admin123!" = 48e7a03e4ef52df853e4e1e87a58c69d1a41a03dcb18590355e36aef4cc5b91c
    INSERT INTO AppUsers (Username, PasswordHash, DisplayName, [Role], IsActive)
    VALUES ('admin', '48e7a03e4ef52df853e4e1e87a58c69d1a41a03dcb18590355e36aef4cc5b91c', 'Administrador', 'Administrador', 1);
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AuditEntries')
BEGIN
    CREATE TABLE AuditEntries (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL DEFAULT '',
        [Action] NVARCHAR(50) NOT NULL DEFAULT '',
        Detail NVARCHAR(MAX) NULL,
        IpAddress NVARCHAR(50) NULL,
        [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_AuditEntries_Timestamp ON AuditEntries ([Timestamp] DESC);
END
GO
