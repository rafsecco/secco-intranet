# syntax=docker/dockerfile:1
# Build multi-estágio (.NET 10) do monolito Secco.Intranet.Web (ADR-0002).
# Contexto de build: a RAIZ deste repositório (Central Package Management exige os
# props/nuget.config na raiz):
#   docker build -f src/Secco.Intranet.Web/Dockerfile -t intranet-web \
#     --secret id=nuget_pat,env=NUGET_PAT .
#
# O restore usa o feed privado "secco" (GitHub Packages, pacotes Secco.*, ADR-0011), que
# exige credencial. Ela entra por BuildKit secret e só existe no ambiente do comando de
# restore: não vai para arquivo nem para camada da imagem.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# O GitHub Packages aceita qualquer usuário não vazio quando a senha é um PAT.
ARG NUGET_USER=intranet

COPY Directory.Build.props Directory.Packages.props nuget.config Secco.Intranet.slnx ./
# Todo projeto sob src/ entra sozinho, inclusive tema ou RCL de quem adota a Intranet. Projeto
# fora de src/ precisa de COPY próprio aqui e no segundo COPY abaixo.
COPY --parents src/*/*.csproj ./

# A variável NuGetPackageSourceCredentials_<fonte> é lida pelo NuGet só neste processo.
RUN --mount=type=secret,id=nuget_pat,required=true \
    NuGetPackageSourceCredentials_secco="Username=${NUGET_USER};Password=$(cat /run/secrets/nuget_pat)" \
    dotnet restore src/Secco.Intranet.Web/Secco.Intranet.Web.csproj

COPY src/ src/

# Os temas versionam o CSS/JS compilado em wwwroot: a imagem não precisa de Node.
RUN dotnet publish src/Secco.Intranet.Web/Secco.Intranet.Web.csproj \
    --configuration Release --output /app --no-restore /p:UseAppHost=false

# Base glibc (Debian), nunca Alpine: a Secco.SDK.Imaging traz asset nativo glibc.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Documentos e chaves do DataProtection ficam fora de /app para sobreviver à recriação do
# container e ser graváveis pelo usuário não-root.
ENV Intranet__Documentos__Armazenamento__Raiz=/var/lib/intranet/documentos
ENV Intranet__ProtecaoDeDados__PastaDasChaves=/var/lib/intranet/chaves
RUN mkdir -p /var/lib/intranet/documentos /var/lib/intranet/chaves \
    && chown -R $APP_UID /var/lib/intranet
VOLUME /var/lib/intranet/documentos
VOLUME /var/lib/intranet/chaves

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secco.Intranet.Web.dll"]
