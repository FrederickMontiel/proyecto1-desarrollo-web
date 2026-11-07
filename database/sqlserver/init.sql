-- --------------------------------------------------------------------------------------
-- Base y login del Sensor Service.
--
-- A diferencia de PostgreSQL y MongoDB, la imagen de SQL Server no ejecuta scripts al
-- arrancar, así que Compose lanza este archivo con sqlcmd en un contenedor de un solo uso.
-- Es idempotente: puede correr en cada `docker compose up` sin romper nada.
--
-- La instancia conserva también la base del sistema anterior (SwmatrcDb), de donde la
-- herramienta de migración lee los datos. El login del servicio no tiene acceso a ella.
-- --------------------------------------------------------------------------------------

IF DB_ID(N'sensor_db') IS NULL
BEGIN
    CREATE DATABASE sensor_db COLLATE Modern_Spanish_CI_AS;
    PRINT N'Base sensor_db creada.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'sensor_svc')
    CREATE LOGIN sensor_svc WITH PASSWORD = N'$(SENSOR_DB_PASSWORD)', CHECK_POLICY = ON;
ELSE
    -- Si la contraseña cambió en el .env, el login se actualiza en el siguiente arranque.
    ALTER LOGIN sensor_svc WITH PASSWORD = N'$(SENSOR_DB_PASSWORD)';
GO

USE sensor_db;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'sensor_svc')
    CREATE USER sensor_svc FOR LOGIN sensor_svc;
GO

-- db_owner sobre su propia base: el servicio aplica sus migraciones de EF Core al arrancar.
ALTER ROLE db_owner ADD MEMBER sensor_svc;
GO
