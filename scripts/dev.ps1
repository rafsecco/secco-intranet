<#
.SYNOPSIS
    Sobe a Secco.Intranet em desenvolvimento com as variáveis do .env.

.DESCRIPTION
    O .env da raiz guarda as credenciais de DEV, inclusive a connection string do tenant, cujo
    nome carrega os hífens do GUID (Secco__Tenancy__Tenants__<id>__ConnectionString). Nenhum
    shell aceita esse nome num export comum, e o valor vem entre aspas para proteger os símbolos
    da senha. Este script lê o arquivo, tira as aspas, exporta tudo só para este processo, sobe
    o SQL de DEV e roda a aplicação.

.PARAMETER Tema
    Tema a usar (Vertical ou Horizontal). Sem ele, vale o de appsettings.

.PARAMETER SemDocker
    Não roda "docker compose up -d" (o SQL já está no ar, ou você usa outro).

.EXAMPLE
    ./scripts/dev.ps1
    ./scripts/dev.ps1 -Tema Horizontal -SemDocker
#>
[CmdletBinding()]
param(
    [ValidateSet('Vertical', 'Horizontal')]
    [string] $Tema,

    [switch] $SemDocker
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$arquivo = Join-Path $raiz '.env'

if (-not (Test-Path $arquivo)) {
    throw "Não achei $arquivo. Copie o .env.example para .env e troque as senhas (ver README)."
}

foreach ($linha in Get-Content $arquivo) {
    $texto = $linha.Trim()

    if ($texto -eq '' -or $texto.StartsWith('#')) {
        continue
    }

    $igual = $texto.IndexOf('=')

    if ($igual -lt 1) {
        continue
    }

    $nome = $texto.Substring(0, $igual).Trim()
    $valor = $texto.Substring($igual + 1).Trim()

    # Aspas simples ou duplas em volta do valor são do formato do .env, não do valor.
    if ($valor.Length -ge 2 -and (($valor[0] -eq "'" -and $valor[-1] -eq "'") -or ($valor[0] -eq '"' -and $valor[-1] -eq '"'))) {
        $valor = $valor.Substring(1, $valor.Length - 2)
    }

    [Environment]::SetEnvironmentVariable($nome, $valor, 'Process')
}

if ($Tema) {
    $env:Intranet__Theme__Nome = $Tema
}

if (-not $SemDocker) {
    docker compose --project-directory $raiz up -d

    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up falhou — o Docker está rodando?'
    }
}

dotnet run --project (Join-Path $raiz 'src/Secco.Intranet.Web') --launch-profile http
