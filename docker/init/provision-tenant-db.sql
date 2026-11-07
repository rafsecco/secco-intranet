-- Provisionamento do banco de dev do tenant "alfa" (ADR-0007): cria o banco e um login de
-- aplicação com db_owner só nele — nunca sa, nunca privilégio de servidor. Segue o mesmo
-- teto de privilégio que a ADR-0028 da plataforma define para o provisionamento real
-- (Secco.SecureGate.Client, ProvisionTenantDatabaseAsync).
--
-- Idempotente: reexecutar (docker compose up com .dados/ já preenchida) não falha nem duplica.
-- AppPassword chega via sqlcmd -v (variável de scripting, não segredo em texto no arquivo).

IF DB_ID(N'intranet_alfa') IS NULL
BEGIN
    CREATE DATABASE intranet_alfa;
END
GO

IF SUSER_ID(N'intranet_alfa_app') IS NULL
BEGIN
    CREATE LOGIN intranet_alfa_app WITH PASSWORD = N'$(AppPassword)';
END
GO

USE intranet_alfa;
GO

IF USER_ID(N'intranet_alfa_app') IS NULL
BEGIN
    CREATE USER intranet_alfa_app FOR LOGIN intranet_alfa_app;
END
GO

ALTER ROLE db_owner ADD MEMBER intranet_alfa_app;
GO
