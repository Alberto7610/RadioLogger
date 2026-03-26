-- Tabla para almacenar logs de los equipos WPF (retención 15 días)
CREATE TABLE LogEntries (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    MachineId NVARCHAR(100) NOT NULL,
    [Timestamp] DATETIME2 NOT NULL,
    [Level] NVARCHAR(10) NOT NULL,
    [Source] NVARCHAR(200) NULL,
    [Message] NVARCHAR(MAX) NOT NULL,
    [Exception] NVARCHAR(MAX) NULL
);

CREATE NONCLUSTERED INDEX IX_LogEntries_Machine_Timestamp
    ON LogEntries (MachineId, [Timestamp] DESC);

GO
