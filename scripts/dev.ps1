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
    Tema a usar: qualquer tema registrado, inclusive o de um fork. Sem ele, vale o de appsettings.
    O nome não é validado aqui: nome errado aparece na primeira página, quando a view do tema não
    é encontrada.

.PARAMETER SemDocker
    Não sobe o SQL de DEV (ele já está no ar, ou você usa outro). Com a instância existente ativa
    no .env (COMPOSE_FILE com docker-compose.sql-existente.yml), o script já pula o Docker sozinho.

.EXAMPLE
    ./scripts/dev.ps1
    ./scripts/dev.ps1 -Tema Horizontal -SemDocker
#>
[CmdletBinding()]
param(
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

if ($env:COMPOSE_FILE -like '*sql-existente*') {
    Write-Host 'Instância de SQL existente ativa no .env: sem subir o SQL de DEV.'
    $SemDocker = $true
}

if (-not $SemDocker) {
    # O docker escreve o progresso no stderr; com 'Stop' o PowerShell trata isso como erro e
    # aborta mesmo com o compose dando certo. Quem decide o sucesso é o código de saída.
    $preferenciaAnterior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    # 2>&1 + ForEach-Object: o progresso sai como texto comum, não como ErrorRecord vermelho.
    docker compose --project-directory $raiz up -d sqlserver-init 2>&1 | ForEach-Object { "$_" }
    $codigoDoCompose = $LASTEXITCODE

    # O up não espera o init terminar, e até lá o banco pode não estar legível.
    if ($codigoDoCompose -eq 0) {
        docker compose --project-directory $raiz wait sqlserver-init 2>&1 | ForEach-Object { "$_" }
        $codigoDoCompose = $LASTEXITCODE
    }

    $ErrorActionPreference = $preferenciaAnterior

    if ($codigoDoCompose -ne 0) {
        throw 'O SQL de DEV não subiu — o Docker está rodando? Veja docker compose logs sqlserver-init.'
    }
}

dotnet run --project (Join-Path $raiz 'src/Secco.Intranet.Web') --launch-profile http
