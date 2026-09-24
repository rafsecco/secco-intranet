using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido de importação: o texto do CSV, já decodificado.</summary>
/// <param name="Csv">Conteúdo do arquivo.</param>
public sealed record ImportarDiretorioCommand(string Csv);

/// <summary>O que acontece (ou aconteceu) com uma linha.</summary>
public enum StatusDaLinha
{
	/// <summary>Cria um perfil novo.</summary>
	Criar = 0,

	/// <summary>Altera um perfil existente.</summary>
	Atualizar = 1,

	/// <summary>Nada muda.</summary>
	SemAlteracao = 2,

	/// <summary>A linha tem erro e não é aplicada.</summary>
	Erro = 3,
}

/// <summary>Uma linha do relatório de importação.</summary>
/// <param name="Numero">Número do registro no arquivo.</param>
/// <param name="Email">E-mail da linha (vazio se a linha nem chegou a ter).</param>
/// <param name="Status">O que acontece com ela.</param>
/// <param name="Erro">Motivo, quando <see cref="StatusDaLinha.Erro"/>.</param>
public sealed record LinhaDoRelatorio(int Numero, string Email, StatusDaLinha Status, string? Erro);

/// <summary>Relatório da pré-visualização ou da aplicação.</summary>
/// <param name="Linhas">Uma entrada por linha do arquivo, em ordem.</param>
/// <param name="Aplicado">Falso na pré-visualização; verdadeiro depois de gravar.</param>
public sealed record RelatorioDeImportacao(IReadOnlyList<LinhaDoRelatorio> Linhas, bool Aplicado)
{
	/// <summary>Perfis que serão (ou foram) criados.</summary>
	public int Criados => Linhas.Count(linha => linha.Status == StatusDaLinha.Criar);

	/// <summary>Perfis que serão (ou foram) alterados.</summary>
	public int Atualizados => Linhas.Count(linha => linha.Status == StatusDaLinha.Atualizar);

	/// <summary>Linhas que não mudam nada.</summary>
	public int SemAlteracao => Linhas.Count(linha => linha.Status == StatusDaLinha.SemAlteracao);

	/// <summary>Linhas com erro.</summary>
	public int ComErro => Linhas.Count(linha => linha.Status == StatusDaLinha.Erro);
}

/// <summary>
/// Importa o diretório de um CSV. <b>Não cria usuário</b>: o e-mail precisa ser de um usuário ativo
/// do SecureGate. Duas etapas sobre o mesmo planejamento: <see cref="PrevisualizarAsync"/> só valida
/// e conta; <see cref="AplicarAsync"/> planeja <b>de novo</b> (nunca confia numa pré-visualização
/// antiga) e grava as linhas válidas. Célula vazia significa "não alterar".
/// </summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ImportarDiretorioHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores,
	ITrilhaDeAuditoria trilha)
{
	private sealed record LinhaPlanejada(
		LinhaDoRelatorio Relatorio,
		Guid UsuarioId,
		string? Nome,
		string? Cargo,
		string? Ramal,
		Guid? SetorId,
		Guid? GestorId);

	/// <summary>Valida o arquivo e conta o que aconteceria — sem gravar nada.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RelatorioDeImportacao>> PrevisualizarAsync(ImportarDiretorioCommand command, CancellationToken cancellationToken = default)
	{
		var plano = await PlanejarAsync(command, cancellationToken).ConfigureAwait(false);

		return plano.IsFailure
			? Result.Failure<RelatorioDeImportacao>(plano.Error)
			: new RelatorioDeImportacao([.. plano.Value.Select(linha => linha.Relatorio)], Aplicado: false);
	}

	/// <summary>Planeja de novo e grava as linhas válidas; audita uma vez, com os totais.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RelatorioDeImportacao>> AplicarAsync(ImportarDiretorioCommand command, CancellationToken cancellationToken = default)
	{
		var plano = await PlanejarAsync(command, cancellationToken).ConfigureAwait(false);

		if (plano.IsFailure)
		{
			return Result.Failure<RelatorioDeImportacao>(plano.Error);
		}

		foreach (var linha in plano.Value.Where(linha => linha.Relatorio.Status is StatusDaLinha.Criar or StatusDaLinha.Atualizar))
		{
			await EdicaoDePerfil.AplicarAsync(
				perfis,
				linha.UsuarioId,
				perfil => [
					.. perfil.EditarContato(linha.Nome ?? perfil.NomeExibicao, linha.Ramal ?? perfil.Ramal, perfil.Sobre),
					.. perfil.EditarDadosFuncionais(linha.Cargo ?? perfil.Cargo, linha.SetorId ?? perfil.SetorId, linha.GestorId ?? perfil.GestorUsuarioId),
				],
				cancellationToken).ConfigureAwait(false);
		}

		var relatorio = new RelatorioDeImportacao([.. plano.Value.Select(linha => linha.Relatorio)], Aplicado: true);

		await trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				VerbosDeAuditoria.DiretorioImportar,
				RecursosDeAuditoria.Diretorio,
				"importacao",
				JsonSerializer.Serialize(new
				{
					criados = relatorio.Criados,
					atualizados = relatorio.Atualizados,
					semAlteracao = relatorio.SemAlteracao,
					comErro = relatorio.ComErro,
				})),
			cancellationToken).ConfigureAwait(false);

		return relatorio;
	}

	private async Task<Result<IReadOnlyList<LinhaPlanejada>>> PlanejarAsync(ImportarDiretorioCommand command, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(command);

		var leitura = LeitorDeCsvDoDiretorio.Ler(command.Csv);

		if (leitura.IsFailure)
		{
			return Result.Failure<IReadOnlyList<LinhaPlanejada>>(leitura.Error);
		}

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<IReadOnlyList<LinhaPlanejada>>(ativos.Error);
		}

		var porEmail = ativos.Value
			.Where(usuario => !string.IsNullOrWhiteSpace(usuario.Email))
			.GroupBy(usuario => usuario.Email, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(grupo => grupo.Key, grupo => grupo.First(), StringComparer.OrdinalIgnoreCase);

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var setorPorSlug = todosOsSetores
			.Where(setor => setor.Ativo)
			.ToDictionary(setor => setor.Slug, setor => setor.Id, StringComparer.OrdinalIgnoreCase);

		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var perfilPorUsuario = todosOsPerfis.GroupBy(perfil => perfil.UsuarioId).ToDictionary(grupo => grupo.Key, grupo => grupo.First());
		var mapaDeGestores = todosOsPerfis
			.Where(perfil => perfil.GestorUsuarioId is not null)
			.ToDictionary(perfil => perfil.UsuarioId, perfil => perfil.GestorUsuarioId!.Value);

		var planejadas = new List<LinhaPlanejada>();
		var vistos = new HashSet<Guid>();

		foreach (var erro in leitura.Value.Erros)
		{
			planejadas.Add(Rejeitada(erro.Numero, string.Empty, erro.Mensagem));
		}

		foreach (var linha in leitura.Value.Linhas)
		{
			planejadas.Add(Planejar(linha, porEmail, setorPorSlug, perfilPorUsuario, mapaDeGestores, vistos));
		}

		return planejadas.OrderBy(linha => linha.Relatorio.Numero).ToList();
	}

	private static LinhaPlanejada Planejar(
		LinhaDoCsv linha,
		Dictionary<string, UsuarioParaDiretorio> porEmail,
		Dictionary<string, Guid> setorPorSlug,
		Dictionary<Guid, PerfilColaborador> perfilPorUsuario,
		Dictionary<Guid, Guid> mapaDeGestores,
		HashSet<Guid> vistos)
	{
		if (!porEmail.TryGetValue(linha.Email, out var usuario))
		{
			return Rejeitada(linha.Numero, linha.Email, "o e-mail não é de um usuário ativo do SecureGate.");
		}

		if (!vistos.Add(usuario.Id))
		{
			return Rejeitada(linha.Numero, linha.Email, "e-mail repetido no arquivo.");
		}

		if ((linha.Nome?.Length ?? 0) > PerfilColaborador.NomeMaxLength
			|| (linha.Cargo?.Length ?? 0) > PerfilColaborador.CargoMaxLength
			|| (linha.Ramal?.Length ?? 0) > PerfilColaborador.RamalMaxLength)
		{
			return Rejeitada(linha.Numero, linha.Email, "nome, cargo ou ramal acima do limite de caracteres.");
		}

		Guid? setorId = null;

		if (linha.SetorSlug is not null)
		{
			if (!setorPorSlug.TryGetValue(linha.SetorSlug, out var encontrado))
			{
				return Rejeitada(linha.Numero, linha.Email, $"o setor '{linha.SetorSlug}' não existe ou está inativo.");
			}

			setorId = encontrado;
		}

		Guid? gestorId = null;

		if (linha.GestorEmail is not null)
		{
			if (!porEmail.TryGetValue(linha.GestorEmail, out var gestor))
			{
				return Rejeitada(linha.Numero, linha.Email, $"o gestor '{linha.GestorEmail}' não é um usuário ativo.");
			}

			if (gestor.Id == usuario.Id)
			{
				return Rejeitada(linha.Numero, linha.Email, "ninguém pode ser gestor de si mesmo.");
			}

			if (RegrasDeGestor.CriariaCiclo(mapaDeGestores, usuario.Id, gestor.Id))
			{
				return Rejeitada(linha.Numero, linha.Email, "essa escolha de gestor criaria um ciclo.");
			}

			// A resolução é sequencial: as linhas seguintes já enxergam este gestor.
			mapaDeGestores[usuario.Id] = gestor.Id;
			gestorId = gestor.Id;
		}

		perfilPorUsuario.TryGetValue(usuario.Id, out var atual);

		var mudou = Mudou(atual?.NomeExibicao, linha.Nome)
			|| Mudou(atual?.Cargo, linha.Cargo)
			|| Mudou(atual?.Ramal, linha.Ramal)
			|| (setorId is not null && setorId != atual?.SetorId)
			|| (gestorId is not null && gestorId != atual?.GestorUsuarioId);

		var status = !mudou ? StatusDaLinha.SemAlteracao : atual is null ? StatusDaLinha.Criar : StatusDaLinha.Atualizar;

		return new LinhaPlanejada(
			new LinhaDoRelatorio(linha.Numero, linha.Email, status, null),
			usuario.Id,
			linha.Nome,
			linha.Cargo,
			linha.Ramal,
			setorId,
			gestorId);
	}

	private static bool Mudou(string? atual, string? novo) =>
		novo is not null && !string.Equals(atual, novo, StringComparison.Ordinal);

	private static LinhaPlanejada Rejeitada(int numero, string email, string motivo) =>
		new(new LinhaDoRelatorio(numero, email, StatusDaLinha.Erro, motivo), Guid.Empty, null, null, null, null, null);
}
