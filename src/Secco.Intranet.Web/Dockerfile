# Build multi-estágio (.NET 10) do monolito Secco.Intranet.Web (ADR-0002).
# Contexto de build: a RAIZ deste repositório (Central Package Management exige os
# props/nuget.config na raiz):
#   docker build -f src/Secco.Intranet.Web/Dockerfile -t secco-intranet-web .
#
# ATENÇÃO: o restore usa o nuget.config da raiz, que aponta o feed privado "secco"
# (GitHub Packages) para os pacotes Secco.* (ADR-0011) — esse feed exige credencial.
# Este Dockerfile ainda NÃO resolve a credencial: forneça-a via build secret
# (`docker build --secret id=nuget_pat,env=NUGET_PAT` + um passo
# `dotnet nuget update source secco --username <user> --password $NUGET_PAT
# --store-password-in-clear-text` antes do restore) ou aponte o nuget.config para um
# feed local/mirror acessível sem autenticação. Sem isso, o `dotnet restore` abaixo
# falha. Por isso o serviço ainda não foi adicionado ao docker-compose.yml — fica
# pendente até o CI ter a credencial configurada.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config Secco.Intranet.slnx ./
COPY src/Secco.Intranet.Domain/Secco.Intranet.Domain.csproj src/Secco.Intranet.Domain/
COPY src/Secco.Intranet.Application/Secco.Intranet.Application.csproj src/Secco.Intranet.Application/
COPY src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj src/Secco.Intranet.Infrastructure/
COPY src/Secco.Intranet.Migrations.SqlServer/Secco.Intranet.Migrations.SqlServer.csproj src/Secco.Intranet.Migrations.SqlServer/
COPY src/Secco.Intranet.Migrations.Postgres/Secco.Intranet.Migrations.Postgres.csproj src/Secco.Intranet.Migrations.Postgres/
COPY src/Secco.Intranet.Web/Secco.Intranet.Web.csproj src/Secco.Intranet.Web/

RUN dotnet restore src/Secco.Intranet.Web/Secco.Intranet.Web.csproj

COPY src/Secco.Intranet.Domain/ src/Secco.Intranet.Domain/
COPY src/Secco.Intranet.Application/ src/Secco.Intranet.Application/
COPY src/Secco.Intranet.Infrastructure/ src/Secco.Intranet.Infrastructure/
COPY src/Secco.Intranet.Migrations.SqlServer/ src/Secco.Intranet.Migrations.SqlServer/
COPY src/Secco.Intranet.Migrations.Postgres/ src/Secco.Intranet.Migrations.Postgres/
COPY src/Secco.Intranet.Web/ src/Secco.Intranet.Web/

RUN dotnet publish src/Secco.Intranet.Web/Secco.Intranet.Web.csproj \
    --configuration Release --output /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secco.Intranet.Web.dll"]
