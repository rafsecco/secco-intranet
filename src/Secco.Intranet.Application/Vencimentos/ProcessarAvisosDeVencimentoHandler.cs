using System.Globalization;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Domain.Vencimentos;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>
/// Varre os itens de vencimento ativos e envia o aviso do dia pelo NotificationHub. O registro
/// do marco é gravado <b>antes</b> do envio: o índice único é o que impede dois processos de
/// avisarem o mesmo marco, e o envio que falha desfaz o registro para a próxima varredura
/// tentar de novo. Se um item tem mais de um lote e um posterior falha depois de um anterior
/// ter saído, o registro também é desfeito — o lote que saiu será reenviado na próxima
/// execução, custo aceito em troca de nunca perder um aviso.
/// </summary>
/// <param name="repository">Persistência de itens e avisos tratados.</param>
/// <param name="diretorio">Cadastro de usuários do tenant, lido uma vez por execução.</param>
/// <param name="notificador">Envio de avisos.</param>
/// <param name="notificacaoOptions">Configuração do aviso, de onde sai a URL base do link.</param>
/// <param name="relogio">Relógio do produto, que define o "hoje".</param>
/// <param name="logger">Log.</param>
public sealed class ProcessarAvisosDeVencimentoHandler(
	IItemVencimentoRepository repository,
	IDiretorioDeUsuarios diretorio,
	INotificadorDeMensagens notificador,
	NotificacaoOptions notificacaoOptions,
	RelogioDeVencimentos relogio,
	ILogger<ProcessarAvisosDeVencimentoHandler> logger)
{
	private static readonly CultureInfo CulturaDoProduto = CultureInfo.GetCultureInfo("pt-BR");

	/// <summary>Executa uma varredura.</summary>
	/// <param name="ct">Token de cancelamento.</param>
	/// <returns>Quantos itens tiveram o aviso enviado (não a quantidade de lotes).</returns>
	public async Task<int> HandleAsync(CancellationToken ct = default)
	{
		var itens = await repository.ListarAtivosParaAvisoAsync(ct).ConfigureAwait(false);
		var hoje = relogio.Hoje();

		IReadOnlyList<UsuarioDoTenant>? usuarios = null;
		var enviados = 0;

		foreach (var candidato in itens)
		{
			var item = candidato.Item;
			var decisao = RegrasDeAviso.Decidir(item, hoje, candidato.MarcosTratados);

			foreach (var marco in decisao.MarcosAPular)
			{
				await RegistrarAsync(item, marco, ResultadoDoAviso.Pulado, 0, ct).ConfigureAwait(false);
			}

			if (decisao.MarcoAEnviar is not { } marcoAEnviar)
			{
				continue;
			}

			// Lida uma vez por execução, e só se alguém precisar dela.
			usuarios ??= await diretorio.ListarDoTenantAtualAsync(ct).ConfigureAwait(false);

			if (usuarios.Count == 0)
			{
				// O adaptador devolve lista vazia tanto para tenant sem usuários quanto para falha
				// do SecureGate; registrar aqui marcaria o marco como sem destinatário para sempre.
				logger.LogWarning(
					"Diretório de usuários vazio ou indisponível; o aviso do marco {Marco} do item de vencimento {ItemId} fica para a próxima execução.",
					marcoAEnviar,
					item.Id);

				continue;
			}

			var destinatarios = AvisoDeVencimento.Destinatarios(usuarios, candidato.SetorSlug, item.ResponsavelId);

			if (destinatarios.Count == 0)
			{
				logger.LogWarning(
					"Nenhum destinatário para o aviso do marco {Marco} do item de vencimento {ItemId} do setor {Setor}.",
					marcoAEnviar,
					item.Id,
					candidato.SetorSlug);

				await RegistrarAsync(item, marcoAEnviar, ResultadoDoAviso.SemDestinatario, 0, ct).ConfigureAwait(false);

				continue;
			}

			if (await EnviarAsync(candidato, decisao, marcoAEnviar, destinatarios, usuarios, ct).ConfigureAwait(false))
			{
				enviados++;
			}
		}

		return enviados;
	}

	private async Task<bool> EnviarAsync(
		ItemParaAviso candidato,
		DecisaoDeAviso decisao,
		int marco,
		IReadOnlyList<UsuarioDoTenant> destinatarios,
		IReadOnlyList<UsuarioDoTenant> usuarios,
		CancellationToken ct)
	{
		var item = candidato.Item;
		var aviso = new AvisoDeVencimentoEnviado(
			item.Id, item.Ciclo, marco, ResultadoDoAviso.Enviado, destinatarios.Count, relogio.Agora());

		if (!await repository.TentarRegistrarAvisoAsync(aviso, ct).ConfigureAwait(false))
		{
			// Outra instância tratou este marco primeiro.
			return false;
		}

		var canais = AvisoDeVencimento.Canais(decisao.DiasRestantes);
		var titulo = AvisoDeVencimento.Titulo(item.Titulo, decisao.DiasRestantes);
		var resumo = Resumir(candidato, usuarios);
		var link = MontarLink(candidato.SetorSlug, item.Id);

		try
		{
			foreach (var (canaisDoLote, destinos) in AvisoDeVencimento.Lotes(destinatarios, canais))
			{
				await notificador
					.EnviarLoteAsync(
						new MensagemParaEnviar(
							titulo, resumo, link, canaisDoLote,
							AvisoDeVencimento.Origem, AvisoDeVencimento.Tipo, destinos, null),
						ct)
					.ConfigureAwait(false);
			}
		}
		catch (NotificacaoIndisponivelException)
		{
			await repository.RemoverAvisoAsync(aviso.Id, ct).ConfigureAwait(false);

			logger.LogWarning(
				"NotificationHub indisponível; o aviso do marco {Marco} do item de vencimento {ItemId} do setor {Setor} será tentado na próxima execução.",
				marco,
				item.Id,
				candidato.SetorSlug);

			return false;
		}
		catch (OperationCanceledException)
		{
			// Desligamento no meio do envio: ninguém recebeu, então o marco volta a ficar pendente.
			// O token da varredura já está cancelado, por isso o desfazer usa CancellationToken.None.
			await repository.RemoverAvisoAsync(aviso.Id, CancellationToken.None).ConfigureAwait(false);

			throw;
		}

		return true;
	}

	private async Task RegistrarAsync(
		ItemVencimento item,
		int marco,
		ResultadoDoAviso resultado,
		int quantidadeDeDestinos,
		CancellationToken ct) =>
		await repository
			.TentarRegistrarAvisoAsync(
				new AvisoDeVencimentoEnviado(item.Id, item.Ciclo, marco, resultado, quantidadeDeDestinos, relogio.Agora()),
				ct)
			.ConfigureAwait(false);

	private static string Resumir(ItemParaAviso candidato, IReadOnlyList<UsuarioDoTenant> usuarios)
	{
		var item = candidato.Item;
		var data = item.DataVencimento.ToString("d 'de' MMMM 'de' yyyy", CulturaDoProduto);
		var resumo = $"Setor: {candidato.SetorNome}. Vencimento: {data}.";

		var email = usuarios.FirstOrDefault(u => u.Id == item.ResponsavelId)?.Email;

		return string.IsNullOrWhiteSpace(email) ? resumo : $"{resumo} Responsável: {email}.";
	}

	private string MontarLink(string setorSlug, Guid itemId)
	{
		var caminho = $"/{setorSlug}/vencimentos/{itemId}";

		return string.IsNullOrWhiteSpace(notificacaoOptions.UrlBase)
			? caminho
			: $"{notificacaoOptions.UrlBase.TrimEnd('/')}{caminho}";
	}
}
