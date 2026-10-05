#!/usr/bin/env bash
# Sobe a Secco.Intranet em desenvolvimento com as variáveis do .env.
#
# O .env da raiz guarda as credenciais de DEV, inclusive a connection string do tenant, cujo
# nome carrega os hífens do GUID (Secco__Tenancy__Tenants__<id>__ConnectionString): o shell
# não aceita esse nome em `export`, então `set -a; . ./.env` não serve. Este script lê o arquivo,
# tira as aspas do valor, passa tudo pelo `env` (que aceita qualquer nome), sobe o SQL de DEV e
# roda a aplicação.
#
# Uso: ./scripts/dev.sh [--tema Vertical|Horizontal] [--sem-docker]
set -euo pipefail

raiz="$(cd "$(dirname "$0")/.." && pwd)"
arquivo="$raiz/.env"
tema=""
sem_docker=0

while [ $# -gt 0 ]; do
    case "$1" in
        --tema) tema="$2"; shift 2 ;;
        --sem-docker) sem_docker=1; shift ;;
        *) echo "Opção desconhecida: $1" >&2; exit 2 ;;
    esac
done

if [ ! -f "$arquivo" ]; then
    echo "Não achei $arquivo. Copie o .env.example para .env e troque as senhas (ver README)." >&2
    exit 1
fi

variaveis=()

while IFS= read -r linha || [ -n "$linha" ]; do
    linha="${linha%$'\r'}"
    texto="$(printf '%s' "$linha" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"

    case "$texto" in
        ''|'#'*) continue ;;
    esac

    nome="${texto%%=*}"
    valor="${texto#*=}"

    [ "$nome" = "$texto" ] && continue

    # Aspas simples ou duplas em volta do valor são do formato do .env, não do valor.
    if [ "${#valor}" -ge 2 ]; then
        primeiro="${valor:0:1}"
        ultimo="${valor: -1}"
        if { [ "$primeiro" = "'" ] && [ "$ultimo" = "'" ]; } || { [ "$primeiro" = '"' ] && [ "$ultimo" = '"' ]; }; then
            valor="${valor:1:${#valor}-2}"
        fi
    fi

    variaveis+=("$nome=$valor")
done < "$arquivo"

if [ -n "$tema" ]; then
    variaveis+=("Intranet__Theme__Nome=$tema")
fi

if [ "$sem_docker" -eq 0 ]; then
    docker compose --project-directory "$raiz" up -d
fi

exec env "${variaveis[@]}" dotnet run --project "$raiz/src/Secco.Intranet.Web" --launch-profile http
