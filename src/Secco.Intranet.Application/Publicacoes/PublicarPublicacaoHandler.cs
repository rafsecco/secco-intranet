using Markdig;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Comando de publicação.</summary>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Corpo">Corpo em Markdown.</param>
/// <param name="Tipo">Natureza.</param>
/// <param name="Visibilidade">Quem enxerga.</param>
/// <param name="Prioridade">Urgência.</param>
/// <param name="PublicadoEm">Entrada no ar.</param>
/// <param name="ExpiraEm">Expiração.</param>
/// <param name="CriadoPor">Quem publica, como aparece na tela.</param>
/// <param name="CriadoPorId">
/// Identificador de quem publica, para excluí-lo do próprio aviso. Não é persistido: casar
/// nome de exibição com usuário seria frágil, e guardar o id exigiria migration sem uso —
/// edição não re-notifica.
/// </param>
public sealed record PublicarPublicacaoCommand(
	string? SetorSlug,
	string? Titulo,
	string? Corpo,
	TipoPublicacao Tipo,
	Visibilidade Visibilidade,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	DateTimeOffset? ExpiraEm,
	string CriadoPor,
	Guid? CriadoPorId = null);

/// <summary>A publicação criada e o que aconteceu com o aviso dela.</summary>
/// <param name="Publicacao">A publicação persistida.</param>
/// <param name="Notificacao">Relatório do aviso.</param>
public sealed record PublicacaoPublicadaDto(PublicacaoDto Publicacao, RelatorioDeNotificacao Notificacao);

/// <summary>
/// Cria uma publicação no setor informado e avisa quem pode vê-la. Notificar nunca derruba
/// publicar: a publicação é o que o usuário pediu, o aviso é consequência.
/// </summary>
/// <param name="repository">Persistência de publicações.</param>
/// <param name="setorRepository">Consulta de setores.</param>
/// <param name="limites">Limites de entrada do produto.</param>
/// <param name="diretorio">Cadastro de usuários do tenant.</param>
/// <param name="notificador">Envio de avisos.</param>
/// <param name="notificacaoOptions">Configuração do aviso.</param>
public sealed class PublicarPublicacaoHandler(
	IPublicacaoRepository repository,
	ISetorRepository setorRepository,
	IntranetOptions limites,
	IDiretorioDeUsuarios diretorio,
	INotificadorDeMensagens notificador,
	NotificacaoOptions notificacaoOptions)
{
	private static readonly MarkdownPipeline PipelineDeTexto = new MarkdownPipelineBuilder()
		.DisableHtml()
		.Build();

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoPublicadaDto>> HandleAsync(
		PublicarPublicacaoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var erro = ValidacaoDePublicacao.Validar(
			command.Titulo, command.Corpo, command.PublicadoEm, command.ExpiraEm, limites.MaxNameLength);

		if (erro is not null)
		{
			return erro;
		}

		if (string.IsNullOrWhiteSpace(command.SetorSlug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var setor = await setorRepository.GetBySlugAsync(command.SetorSlug, cancellationToken).ConfigureAwait(false);

		if (setor is null || !setor.Ativo)
		{
			return IntranetErrors.Setores.NotFound;
		}

		var publicacao = new Publicacao(
			setor.Id, command.Titulo!, command.Corpo!, command.Tipo, command.Visibilidade,
			command.Prioridade, command.PublicadoEm, command.ExpiraEm, command.CriadoPor);

		await repository.AddAsync(publicacao, cancellationToken).ConfigureAwait(false);

		var dto = Projecao.De(new PublicacaoComSetor(publicacao, setor.Nome, setor.Slug));
		var relatorio = await AvisarAsync(dto, command.CriadoPorId, cancellationToken).ConfigureAwait(false);

		return new PublicacaoPublicadaDto(dto, relatorio);
	}

	private async Task<RelatorioDeNotificacao> AvisarAsync(
		PublicacaoDto publicacao,
		Guid? autorId,
		CancellationToken cancellationToken)
	{
		// Publicação agendada não sai sem aviso, e o aviso não sai antes da hora: o Hub
		// entrega no instante pedido, segurando e-mail E item do sino (secco-platform#24).
		var programadaPara = publicacao.PublicadoEm > DateTimeOffset.UtcNow
			? publicacao.PublicadoEm
			: (DateTimeOffset?)null;

		var canais = CanaisDaPrioridade.De(publicacao.Prioridade);
		var usuarios = await diretorio.ListarDoTenantAtualAsync(cancellationToken).ConfigureAwait(false);

		var destinatarios = AvisoDePublicacao.Destinatarios(
			usuarios, publicacao.Visibilidade, publicacao.SetorSlug, autorId);

		var (validos, semEmail) = AvisoDePublicacao.Particionar(destinatarios, canais);

		if (validos.Count == 0)
		{
			return new RelatorioDeNotificacao(0, semEmail, programadaPara, false);
		}

		var resumo = Resumir(publicacao.Corpo);
		var link = MontarLink(publicacao.Id);

		try
		{
			foreach (var bloco in AvisoDePublicacao.Fatiar(validos))
			{
				await notificador
					.EnviarLoteAsync(
						new MensagemParaEnviar(
							publicacao.Titulo, resumo, link, canais,
							AvisoDePublicacao.Origem, publicacao.Id.ToString(), bloco, programadaPara),
						cancellationToken)
					.ConfigureAwait(false);
			}
		}
		catch (NotificacaoIndisponivelException)
		{
			return RelatorioDeNotificacao.DeIndisponivel(semEmail);
		}

		return new RelatorioDeNotificacao(validos.Count, semEmail, programadaPara, false);
	}

	private string MontarLink(Guid publicacaoId)
	{
		var caminho = $"/publicacoes/{publicacaoId}";

		return string.IsNullOrWhiteSpace(notificacaoOptions.UrlBase)
			? caminho
			: $"{notificacaoOptions.UrlBase.TrimEnd('/')}{caminho}";
	}

	private string Resumir(string corpo)
	{
		// Texto puro: o Hub tem um Message só, servindo sino e e-mail, e markdown cru
		// apareceria literalmente no e-mail.
		var texto = Markdown.ToPlainText(corpo, PipelineDeTexto).Replace('\n', ' ').Trim();

		return texto.Length <= notificacaoOptions.TamanhoDoResumo
			? texto
			: texto[..notificacaoOptions.TamanhoDoResumo].TrimEnd() + "…";
	}
}
