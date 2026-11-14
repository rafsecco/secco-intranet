using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Pedido de edição.</summary>
/// <param name="PublicacaoId">Identificador da publicação.</param>
/// <param name="SetoresAdministrados">Setores que o usuário administra.</param>
/// <param name="ExigirVinculo">Quando <c>false</c>, dispensa a checagem.</param>
/// <param name="Titulo">Novo título.</param>
/// <param name="Corpo">Novo corpo em Markdown.</param>
/// <param name="Tipo">Nova natureza.</param>
/// <param name="Visibilidade">Nova visibilidade.</param>
/// <param name="Prioridade">Nova urgência.</param>
/// <param name="PublicadoEm">Nova entrada no ar.</param>
/// <param name="ExpiraEm">Nova expiração.</param>
/// <param name="Imagem">Imagem nova, ou nulo para manter a atual. O chamador fecha o fluxo.</param>
/// <param name="ImagemDescricao">
/// Descrição da imagem. Com imagem nova, vale para ela; sem, é aplicada à atual (se existir).
/// </param>
/// <param name="RemoverImagem">Tira a imagem atual. Ignorado quando há <paramref name="Imagem"/>.</param>
/// <param name="ImagensDoTexto">Arquivos das imagens novas do texto; o índice é o N de <c>nova-imagem:N</c>.</param>
public sealed record EditarPublicacaoCommand(
	Guid PublicacaoId,
	IReadOnlySet<string> SetoresAdministrados,
	bool ExigirVinculo,
	string? Titulo,
	string? Corpo,
	TipoPublicacao Tipo,
	Visibilidade Visibilidade,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	DateTimeOffset? ExpiraEm,
	ImagemEnviada? Imagem = null,
	string? ImagemDescricao = null,
	bool RemoverImagem = false,
	IReadOnlyList<ImagemEnviada>? ImagensDoTexto = null);

/// <summary>Altera conteúdo e agendamento de uma publicação existente.</summary>
/// <param name="repository">Persistência de publicações.</param>
/// <param name="limites">Limites de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="imagens">Processamento da imagem enviada.</param>
/// <param name="arquivos">Armazenamento cifrado.</param>
/// <param name="imagensNoTexto">Persistência das imagens no texto.</param>
/// <param name="logger">Log.</param>
public sealed class EditarPublicacaoHandler(
	IPublicacaoRepository repository,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha,
	IProcessadorDeImagem imagens,
	IArquivoStore arquivos,
	IImagemNoTextoRepository imagensNoTexto,
	ILogger<EditarPublicacaoHandler> logger)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoDto>> HandleAsync(
		EditarPublicacaoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var erro = ValidacaoDePublicacao.Validar(
			command.Titulo, command.Corpo, command.PublicadoEm, command.ExpiraEm, limites.MaxNameLength);

		if (erro is not null)
		{
			return erro;
		}

		erro = ImagemDaPublicacao.ValidarDescricao(command.ImagemDescricao);

		if (erro is not null)
		{
			return erro;
		}

		var encontrada = await repository.GetByIdAsync(command.PublicacaoId, cancellationToken).ConfigureAwait(false);

		if (encontrada is null
			|| (command.ExigirVinculo && !command.SetoresAdministrados.Contains(encontrada.SetorSlug)))
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		var publicacao = encontrada.Publicacao;

		var existentes = await imagensNoTexto
			.ListarDaPublicacaoAsync(publicacao.Id, cancellationToken)
			.ConfigureAwait(false);

		var analise = ImagensDoCorpo.Analisar(
			command.Corpo, publicacao.Id, existentes.Select(i => i.Id).ToHashSet(), command.ImagensDoTexto?.Count ?? 0);

		if (analise.IsFailure)
		{
			return analise.Error;
		}

		ArquivoGravado? gravada = null;

		if (command.Imagem is not null)
		{
			var gravacao = await ImagemDaPublicacao
				.GravarAsync(command.Imagem, imagens, arquivos, cancellationToken)
				.ConfigureAwait(false);

			if (gravacao.IsFailure)
			{
				return gravacao.Error;
			}

			gravada = gravacao.Value;
		}

		var preparado = await ImagensDoCorpo
			.GravarEReescreverAsync(
				analise.Value, publicacao.Id, command.ImagensDoTexto, imagens, arquivos, logger, cancellationToken)
			.ConfigureAwait(false);

		if (preparado.IsFailure)
		{
			await ImagemDaPublicacao
				.ApagarAsync(gravada?.CaminhoRelativo, arquivos, logger, CancellationToken.None)
				.ConfigureAwait(false);

			return preparado.Error;
		}

		var caminhosNovos = preparado.Value.Novas.Select(i => i.CaminhoRelativo).ToList();

		if (gravada is not null)
		{
			caminhosNovos.Add(gravada.CaminhoRelativo);
		}

		var obsoletas = existentes.Where(i => !analise.Value.Existentes.Contains(i.Id)).ToList();

		var (anterior, acao) = await ImagemDaPublicacao
			.PersistirOuDescartarAsync(
				async () =>
				{
					publicacao.Editar(
						command.Titulo!, preparado.Value.Corpo, command.Tipo, command.Visibilidade,
						command.Prioridade, command.PublicadoEm, command.ExpiraEm);

					string? removida = null;
					var resultado = "manter";

					if (gravada is not null)
					{
						removida = publicacao.DefinirImagem(gravada.CaminhoRelativo, gravada.ChaveEmbrulhada, command.ImagemDescricao);
						resultado = "trocar";
					}
					else if (command.RemoverImagem)
					{
						removida = publicacao.RemoverImagem();
						resultado = removida is null ? "manter" : "remover";
					}
					else if (publicacao.TemImagem && command.ImagemDescricao is not null)
					{
						publicacao.DescreverImagem(command.ImagemDescricao);
					}

					foreach (var imagem in preparado.Value.Novas)
					{
						imagensNoTexto.Adicionar(imagem);
					}

					foreach (var imagem in obsoletas)
					{
						imagensNoTexto.Remover(imagem);
					}

					await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

					return (removida, resultado);
				},
				caminhosNovos,
				arquivos,
				logger)
			.ConfigureAwait(false);

		await ImagemDaPublicacao.ApagarAsync(anterior, arquivos, logger, cancellationToken).ConfigureAwait(false);

		foreach (var imagem in obsoletas)
		{
			await ImagemDaPublicacao.ApagarAsync(imagem.CaminhoRelativo, arquivos, logger, cancellationToken).ConfigureAwait(false);
		}

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.MuralEditar,
					RecursosDeAuditoria.Publicacao,
					encontrada.Publicacao.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = encontrada.Publicacao.Titulo,
						tipo = encontrada.Publicacao.Tipo.ToString(),
						visibilidade = encontrada.Publicacao.Visibilidade.ToString(),
						prioridade = encontrada.Publicacao.Prioridade.ToString(),
						setor = encontrada.SetorSlug,
						imagem = publicacao.TemImagem,
						imagemAcao = acao,
						imagensNoTexto = analise.Value.Existentes.Count + preparado.Value.Novas.Count,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return Projecao.De(encontrada);
	}
}
