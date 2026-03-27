-- Ampliar tabla Licenses para sistema de licenciamiento completo
-- Ejecutar sobre BD RadioLoggerDB

-- Verificar si la tabla existe, si no crearla completa
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Licenses')
BEGIN
    CREATE TABLE Licenses (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        [Key] NVARCHAR(450) NOT NULL,
        LicenseType NVARCHAR(20) NOT NULL DEFAULT 'DEMO',
        ClientName NVARCHAR(MAX) NOT NULL DEFAULT '',
        MachineId NVARCHAR(100) NOT NULL DEFAULT '',
        HardwareId NVARCHAR(200) NOT NULL DEFAULT '',
        MaxSlots INT NOT NULL DEFAULT 4,
        StartDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ExpirationDate DATETIME2 NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        LastCheckIn DATETIME2 NULL,
        Notes NVARCHAR(MAX) NULL
    );
    CREATE UNIQUE INDEX IX_Licenses_Key ON Licenses ([Key]);
    CREATE INDEX IX_Licenses_MachineId ON Licenses (MachineId);
END
ELSE
BEGIN
    -- Agregar columnas nuevas si no existen
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Licenses' AND COLUMN_NAME = 'LicenseType')
        ALTER TABLE Licenses ADD LicenseType NVARCHAR(20) NOT NULL DEFAULT 'DEMO';
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Licenses' AND COLUMN_NAME = 'MachineId')
        ALTER TABLE Licenses ADD MachineId NVARCHAR(100) NOT NULL DEFAULT '';
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Licenses' AND COLUMN_NAME = 'HardwareId')
        ALTER TABLE Licenses ADD HardwareId NVARCHAR(200) NOT NULL DEFAULT '';
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Licenses' AND COLUMN_NAME = 'StartDate')
        ALTER TABLE Licenses ADD StartDate DATETIME2 NOT NULL DEFAULT GETUTCDATE();
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Licenses' AND COLUMN_NAME = 'LastCheckIn')
        ALTER TABLE Licenses ADD LastCheckIn DATETIME2 NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Licenses' AND COLUMN_NAME = 'Notes')
        ALTER TABLE Licenses ADD Notes NVARCHAR(MAX) NULL;

    -- Crear índice si no existe
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Licenses_MachineId')
        CREATE INDEX IX_Licenses_MachineId ON Licenses (MachineId);
END
GO
