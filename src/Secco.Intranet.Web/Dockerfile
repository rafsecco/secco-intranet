# syntax=docker/dockerfile:1
# Build multi-estágio (.NET 10) do monolito Secco.Intranet.Web (ADR-0002).
# Contexto de build: a RAIZ deste repositório (Central Package Management exige os
# props/nuget.config na raiz):
#   docker build -f src/Secco.Intranet.Web/Dockerfile -t secco-intranet-web \
#     --secret id=nuget_pat,env=NUGET_PAT .
#
# O restore usa o feed privado "secco" (GitHub Packages, pacotes Secco.*, ADR-0011), que
# exige credencial. Ela entra por BuildKit secret e só existe no ambiente do comando de
# restore: não vai para arquivo nem para camada da imagem.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# O GitHub Packages aceita qualquer usuário não vazio quando a senha é um PAT.
ARG NUGET_USER=secco-intranet

COPY Directory.Build.props Directory.Packages.props nuget.config Secco.Intranet.slnx ./
COPY src/Secco.Intranet.Domain/Secco.Intranet.Domain.csproj src/Secco.Intranet.Domain/
COPY src/Secco.Intranet.Application/Secco.Intranet.Application.csproj src/Secco.Intranet.Application/
COPY src/Secco.Intranet.Infrastructure/Secco.Intranet.Infrastructure.csproj src/Secco.Intranet.Infrastructure/
COPY src/Secco.Intranet.Migrations.SqlServer/Secco.Intranet.Migrations.SqlServer.csproj src/Secco.Intranet.Migrations.SqlServer/
COPY src/Secco.Intranet.Migrations.Postgres/Secco.Intranet.Migrations.Postgres.csproj src/Secco.Intranet.Migrations.Postgres/
COPY src/Secco.Intranet.Web.Theming/Secco.Intranet.Web.Theming.csproj src/Secco.Intranet.Web.Theming/
COPY src/Secco.Intranet.Web.Extensibility/Secco.Intranet.Web.Extensibility.csproj src/Secco.Intranet.Web.Extensibility/
COPY src/Secco.Intranet.Themes.Vertical/Secco.Intranet.Themes.Vertical.csproj src/Secco.Intranet.Themes.Vertical/
COPY src/Secco.Intranet.Themes.Horizontal/Secco.Intranet.Themes.Horizontal.csproj src/Secco.Intranet.Themes.Horizontal/
COPY src/Secco.Intranet.Web/Secco.Intranet.Web.csproj src/Secco.Intranet.Web/

# A variável NuGetPackageSourceCredentials_<fonte> é lida pelo NuGet só neste processo.
RUN --mount=type=secret,id=nuget_pat,required=true \
    NuGetPackageSourceCredentials_secco="Username=${NUGET_USER};Password=$(cat /run/secrets/nuget_pat)" \
    dotnet restore src/Secco.Intranet.Web/Secco.Intranet.Web.csproj

COPY src/Secco.Intranet.Domain/ src/Secco.Intranet.Domain/
COPY src/Secco.Intranet.Application/ src/Secco.Intranet.Application/
COPY src/Secco.Intranet.Infrastructure/ src/Secco.Intranet.Infrastructure/
COPY src/Secco.Intranet.Migrations.SqlServer/ src/Secco.Intranet.Migrations.SqlServer/
COPY src/Secco.Intranet.Migrations.Postgres/ src/Secco.Intranet.Migrations.Postgres/
COPY src/Secco.Intranet.Web.Theming/ src/Secco.Intranet.Web.Theming/
COPY src/Secco.Intranet.Web.Extensibility/ src/Secco.Intranet.Web.Extensibility/
COPY src/Secco.Intranet.Themes.Vertical/ src/Secco.Intranet.Themes.Vertical/
COPY src/Secco.Intranet.Themes.Horizontal/ src/Secco.Intranet.Themes.Horizontal/
COPY src/Secco.Intranet.Web/ src/Secco.Intranet.Web/

# Os temas versionam o CSS/JS compilado em wwwroot: a imagem não precisa de Node.
RUN dotnet publish src/Secco.Intranet.Web/Secco.Intranet.Web.csproj \
    --configuration Release --output /app --no-restore /p:UseAppHost=false

# Base glibc (Debian), nunca Alpine: a Secco.SDK.Imaging traz asset nativo glibc.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Documentos enviados ficam fora de /app para sobreviver à recriação do container e
# ser graváveis pelo usuário não-root.
ENV Intranet__Documentos__Armazenamento__Raiz=/var/lib/secco-intranet/documentos
RUN mkdir -p /var/lib/secco-intranet/documentos \
    && chown -R $APP_UID /var/lib/secco-intranet
VOLUME /var/lib/secco-intranet/documentos

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secco.Intranet.Web.dll"]
