-- Tablas de usuarios y auditoría para RadioLogger Dashboard

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AppUsers')
BEGIN
    CREATE TABLE AppUsers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        PasswordHash NVARCHAR(MAX) NOT NULL,
        DisplayName NVARCHAR(MAX) NOT NULL DEFAULT '',
        Email NVARCHAR(255) NOT NULL,
        [Role] NVARCHAR(20) NOT NULL DEFAULT 'Operador',
        IsActive BIT NOT NULL DEFAULT 1,
        TelegramChatId NVARCHAR(100) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastLogin DATETIME2 NULL
    );
    CREATE UNIQUE INDEX IX_AppUsers_Username ON AppUsers (Username);
    CREATE INDEX IX_AppUsers_Email ON AppUsers (Email);
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
