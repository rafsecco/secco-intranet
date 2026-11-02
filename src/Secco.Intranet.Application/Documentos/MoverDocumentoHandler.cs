using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Movimento que se pode pedir sobre um item da lista de documentos.</summary>
public enum MovimentoDeDocumento
{
	/// <summary>Troca de lugar com o irmão de cima.</summary>
	Subir = 1,

	/// <summary>Troca de lugar com o irmão de baixo.</summary>
	Descer = 2,

	/// <summary>Vira o último filho do irmão imediatamente acima.</summary>
	Recuar = 3,

	/// <summary>Volta para a raiz, logo depois do pai.</summary>
	Desrecuar = 4,
}

/// <summary>Pedido de movimento de um documento.</summary>
/// <param name="Dono">Dono da rota (setor ou biblioteca); o documento precisa ser dele.</param>
/// <param name="DocumentoId">Identificador do documento.</param>
/// <param name="Acao">Movimento pedido.</param>
public sealed record MoverDocumentoCommand(ReferenciaDeDono Dono, Guid DocumentoId, MovimentoDeDocumento Acao);

/// <summary>
/// Muda um item de lugar na lista do dono. A permissão de escrita é conferida pelo controller;
/// aqui só se garante que o documento é do dono da rota e que o movimento respeita a hierarquia
/// de dois níveis. Cada movimento grava uma única vez, renumerando os irmãos afetados de zero a n-1.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class MoverDocumentoHandler(IDocumentoRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de movimento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(MoverDocumentoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var encontrado = await repository.GetByIdAsync(command.DocumentoId, cancellationToken).ConfigureAwait(false);

		if (encontrado is null
			|| !encontrado.Documento.Ativo
			|| encontrado.Dono != command.Dono)
		{
			return Result.Failure(IntranetErrors.Documentos.NotFound);
		}

		var documento = encontrado.Documento;
		var paiDe = documento.ParentId;
		var de = documento.Ordem;

		var irmaos = await repository
			.ListarIrmaosAsync(documento.Dono, paiDe, cancellationToken)
			.ConfigureAwait(false);
		var indice = IndexOf(irmaos, documento.Id);

		if (indice < 0)
		{
			return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
		}

		Guid? paiPara = paiDe;

		switch (command.Acao)
		{
			case MovimentoDeDocumento.Subir:
			case MovimentoDeDocumento.Descer:
			{
				var alvo = command.Acao == MovimentoDeDocumento.Subir ? indice - 1 : indice + 1;

				if (alvo < 0 || alvo >= irmaos.Count)
				{
					return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
				}

				var lista = irmaos.ToList();
				(lista[indice], lista[alvo]) = (lista[alvo], lista[indice]);
				PosicaoDeDocumentos.Renumerar(lista, paiDe);
				break;
			}

			case MovimentoDeDocumento.Recuar:
			{
				if (documento.EhGrupo || paiDe is not null || indice == 0)
				{
					return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
				}

				if (await repository.TemFilhosAtivosAsync(documento.Id, cancellationToken).ConfigureAwait(false))
				{
					return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
				}

				var acima = irmaos[indice - 1];
				var filhos = await repository
					.ListarIrmaosAsync(documento.Dono, acima.Id, cancellationToken)
					.ConfigureAwait(false);

				paiPara = acima.Id;
				documento.Posicionar(acima.Id, filhos.Count);

				var raiz = irmaos.Where(item => item.Id != documento.Id).ToList();
				PosicaoDeDocumentos.Renumerar(raiz, null);
				break;
			}

			case MovimentoDeDocumento.Desrecuar:
			{
				if (paiDe is null)
				{
					return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
				}

				var raiz = (await repository
					.ListarIrmaosAsync(documento.Dono, null, cancellationToken)
					.ConfigureAwait(false)).ToList();
				var posicaoDoPai = IndexOf(raiz, paiDe.Value);

				// Pai fora da raiz ativa: não há "logo depois do pai" onde pôr o item.
				if (posicaoDoPai < 0)
				{
					return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
				}

				raiz.Insert(posicaoDoPai + 1, documento);
				paiPara = null;
				PosicaoDeDocumentos.Renumerar(raiz, null);

				var restantes = irmaos.Where(item => item.Id != documento.Id).ToList();
				PosicaoDeDocumentos.Renumerar(restantes, paiDe);
				break;
			}

			default:
				return Result.Failure(IntranetErrors.Documentos.MovimentoInvalido);
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoMover,
					RecursosDeAuditoria.Documento,
					documento.Id.ToString(),
					TrilhaDeDono.Serializar(
						encontrado.Dono,
						[],
						[
							("documento", documento.Id.ToString()),
							("acao", command.Acao.ToString().ToLowerInvariant()),
							("paiDe", paiDe?.ToString()),
							("paiPara", paiPara?.ToString()),
							("de", de),
							("para", documento.Ordem),
						])),
				cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}

	private static int IndexOf(IReadOnlyList<Documento> lista, Guid id)
	{
		for (var indice = 0; indice < lista.Count; indice++)
		{
			if (lista[indice].Id == id)
			{
				return indice;
			}
		}

		return -1;
	}
}
