-- Agregar campos nuevos a RegisteredStation
ALTER TABLE RegisteredStations ADD Siglas NVARCHAR(20) NULL;
ALTER TABLE RegisteredStations ADD Frecuencia NVARCHAR(15) NULL;
ALTER TABLE RegisteredStations ADD Banda NVARCHAR(5) NULL;
ALTER TABLE RegisteredStations ADD NombreComercial NVARCHAR(100) NULL;
ALTER TABLE RegisteredStations ADD Estado NVARCHAR(50) NULL;
ALTER TABLE RegisteredStations ADD Plaza NVARCHAR(50) NULL;
ALTER TABLE RegisteredStations ADD GrupoEmpresa NVARCHAR(100) NULL;
ALTER TABLE RegisteredStations ADD Formato NVARCHAR(50) NULL;
ALTER TABLE RegisteredStations ADD Potencia NVARCHAR(20) NULL;
ALTER TABLE RegisteredStations ADD Cobertura NVARCHAR(100) NULL;
ALTER TABLE RegisteredStations ADD Notas NVARCHAR(500) NULL;
ALTER TABLE RegisteredStations ADD Activa BIT NOT NULL DEFAULT 1;
ALTER TABLE RegisteredStations ADD FechaAlta DATETIME2 NOT NULL DEFAULT GETUTCDATE();
