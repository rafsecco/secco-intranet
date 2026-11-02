using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Dono já localizado, ativo e pronto para receber documentos.</summary>
/// <param name="Dono">Dono como a entidade Documento o guarda.</param>
/// <param name="Referencia">Referência pelo slug.</param>
/// <param name="Nome">Nome de exibição do dono.</param>
public sealed record DonoResolvido(DonoDeDocumento Dono, ReferenciaDeDono Referencia, string Nome);

/// <summary>Resolve a referência de um dono (setor ou biblioteca) para o dono ativo correspondente.</summary>
/// <param name="setores">Consulta de setores.</param>
/// <param name="bibliotecas">Consulta de bibliotecas.</param>
public sealed class ResolvedorDeDono(ISetorRepository setores, IBibliotecaRepository bibliotecas)
{
	/// <summary>Resolve a referência.</summary>
	/// <param name="referencia">Referência a resolver.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O dono; <c>null</c> para referência nula, slug vazio, dono inexistente ou inativo.</returns>
	public async Task<DonoResolvido?> ResolverAsync(
		ReferenciaDeDono? referencia,
		CancellationToken cancellationToken = default)
	{
		if (referencia is null || string.IsNullOrWhiteSpace(referencia.Slug))
		{
			return null;
		}

		if (referencia.Tipo == TipoDeDono.Biblioteca)
		{
			var biblioteca = await bibliotecas.GetBySlugAsync(referencia.Slug, cancellationToken).ConfigureAwait(false);

			return biblioteca is { Ativo: true }
				? new DonoResolvido(
					DonoDeDocumento.DaBiblioteca(biblioteca.Id),
					ReferenciaDeDono.DaBiblioteca(biblioteca.Slug),
					biblioteca.Nome)
				: null;
		}

		var setor = await setores.GetBySlugAsync(referencia.Slug, cancellationToken).ConfigureAwait(false);

		return setor is { Ativo: true }
			? new DonoResolvido(DonoDeDocumento.DoSetor(setor.Id), ReferenciaDeDono.DoSetor(setor.Slug), setor.Nome)
			: null;
	}

	/// <summary>Erro de "não encontrado" do tipo de dono da referência.</summary>
	/// <param name="referencia">Referência que não resolveu.</param>
	public static Error NaoEncontrado(ReferenciaDeDono? referencia) =>
		referencia?.Tipo == TipoDeDono.Biblioteca
			? IntranetErrors.Bibliotecas.NotFound
			: IntranetErrors.Setores.NotFound;
}
