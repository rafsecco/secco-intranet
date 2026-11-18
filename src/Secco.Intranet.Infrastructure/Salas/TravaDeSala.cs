using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Infrastructure.Salas;

/// <summary>
/// Trava a linha de uma sala até o fim da transação corrente, para serializar as gravações de
/// reserva da mesma sala e fechar a janela entre "conferir conflito" e "gravar".
/// É a única peça do produto com SQL escrito à mão: o que o EF não expressa (bloqueio de linha)
/// fica aqui. Tabela e coluna de chave saem do modelo do EF e o id vai como parâmetro.
/// </summary>
internal static class TravaDeSala
{
	/// <summary>Trava a sala. Exige uma transação aberta no <paramref name="contexto"/>.</summary>
	/// <exception cref="InvalidOperationException">Sem transação aberta ou engine sem suporte.</exception>
	internal static async Task AdquirirAsync(DbContext contexto, Guid salaId, CancellationToken ct)
	{
		var transacao = contexto.Database.CurrentTransaction
			?? throw new InvalidOperationException("A trava da sala exige uma transação aberta.");

		var sql = MontarSql(contexto);

		await using var comando = contexto.Database.GetDbConnection().CreateCommand();
		comando.Transaction = transacao.GetDbTransaction();
		comando.CommandText = sql;

		var parametro = comando.CreateParameter();
		parametro.ParameterName = "@id";
		parametro.Value = salaId;
		comando.Parameters.Add(parametro);

		// Lê o resultado só para a consulta executar; a linha é irrelevante.
		await comando.ExecuteScalarAsync(ct).ConfigureAwait(false);
	}

	private static string MontarSql(DbContext contexto)
	{
		var tipo = contexto.Model.FindEntityType(typeof(Sala))
			?? throw new InvalidOperationException("Sala não está no modelo do EF.");

		var tabela = tipo.GetTableName()
			?? throw new InvalidOperationException("Sala não está mapeada para uma tabela.");

		var objeto = StoreObjectIdentifier.Table(tabela, tipo.GetSchema());
		var coluna = tipo.FindPrimaryKey()!.Properties.Single().GetColumnName(objeto)
			?? throw new InvalidOperationException("A chave de Sala não está mapeada para uma coluna.");

		if (contexto.Database.IsNpgsql())
		{
			var nome = Qualificar(tipo.GetSchema(), tabela, Aspas);
			return $"SELECT 1 FROM {nome} WHERE {Aspas(coluna)} = @id FOR UPDATE";
		}

		if (contexto.Database.IsSqlServer())
		{
			var nome = Qualificar(tipo.GetSchema(), tabela, Colchetes);
			return $"SELECT 1 FROM {nome} WITH (UPDLOCK, ROWLOCK) WHERE {Colchetes(coluna)} = @id";
		}

		throw new InvalidOperationException($"A trava da sala não tem suporte para o provedor {contexto.Database.ProviderName}.");
	}

	private static string Qualificar(string? esquema, string tabela, Func<string, string> citar) =>
		esquema is null ? citar(tabela) : $"{citar(esquema)}.{citar(tabela)}";

	private static string Aspas(string identificador) => "\"" + identificador.Replace("\"", "\"\"") + "\"";

	private static string Colchetes(string identificador) => "[" + identificador.Replace("]", "]]") + "]";
}
