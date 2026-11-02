using System.Text.Json;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>Comando de criação de uma biblioteca de documentos.</summary>
/// <param name="Nome">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto. Obrigatório, único por tenant, imutável.</param>
/// <param name="Icone">Classe do Bootstrap Icons; vazio usa o padrão.</param>
/// <param name="TodosLeem">Se todo membro do tenant passa a ler a biblioteca (read no perfil implícito).</param>
public sealed record CriarBibliotecaCommand(string? Nome, string? Slug, string? Icone, bool TodosLeem);

/// <summary>
/// Caso de uso: valida tudo, concede a leitura a todos no SecureGate quando pedido (ADR-0001) e
/// só então persiste a entidade — erros de negócio fluem por <see cref="Result{T}"/>, nunca por
/// exceção (ADR-0004).
/// </summary>
/// <param name="repository">Persistência de bibliotecas.</param>
/// <param name="options">Limites de entrada do produto.</param>
/// <param name="gestao">Gestão de acesso no SecureGate.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="slugsReservados">Slugs que as rotas fixas de <c>/bibliotecas</c> já ocupam.</param>
public sealed class CriarBibliotecaHandler(
	IBibliotecaRepository repository,
	IntranetOptions options,
	IGestaoDeAcesso gestao,
	ITrilhaDeAuditoria trilha,
	ISlugsReservadosDeBiblioteca slugsReservados)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<BibliotecaDto>> HandleAsync(
		CriarBibliotecaCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return IntranetErrors.Bibliotecas.NomeRequired;
		}

		if (command.Nome.Length > options.MaxNameLength)
		{
			return IntranetErrors.Bibliotecas.NomeTooLong(options.MaxNameLength);
		}

		if (string.IsNullOrWhiteSpace(command.Slug))
		{
			return IntranetErrors.Bibliotecas.SlugRequired;
		}

		// A entidade recusa slug e ícone fora do formato com exceção, que é a rede de segurança
		// para chamador interno. Entrada de usuário vira Result (ADR-0004), então a checagem
		// acontece aqui — e antes de chamar o SecureGate, para não conceder acesso por causa de
		// um campo mal digitado.
		if (!Biblioteca.SlugEhValido(command.Slug))
		{
			return IntranetErrors.Bibliotecas.SlugInvalido;
		}

		var slug = command.Slug.Trim().ToLowerInvariant();

		// A biblioteca mora em /bibliotecas/{slug}; um slug igual a uma rota fixa ficaria inalcançável.
		if (slugsReservados.Contem(slug))
		{
			return IntranetErrors.Bibliotecas.SlugReservado(slug);
		}

		if (!Biblioteca.IconeEhValido(command.Icone))
		{
			return IntranetErrors.Bibliotecas.IconeInvalido;
		}

		if (await repository.ExistsBySlugAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			return IntranetErrors.Bibliotecas.SlugAlreadyExists(slug);
		}

		// ADR-0001: o acesso no SecureGate é concedido ANTES de persistir a biblioteca. A
		// permissão órfã (se algo falhar depois) é inofensiva e a operação é idempotente
		// (mesclagem) — tentar de novo não duplica nada. O inverso — biblioteca persistida que
		// "todos leem" sem a permissão — deixaria a biblioteca invisível sem ninguém perceber.
		if (command.TodosLeem)
		{
			var garantido = await gestao
				.GarantirPermissoesAsync(
					ClassificacaoDePerfil.PerfilImplicito,
					[IntranetPermissoes.Biblioteca.Read(slug)],
					cancellationToken)
				.ConfigureAwait(false);

			if (garantido.IsFailure)
			{
				return garantido.Error;
			}
		}

		var biblioteca = new Biblioteca(command.Nome, slug, command.Icone);

		await repository.AddAsync(biblioteca, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.BibliotecaCriar,
					RecursosDeAuditoria.Biblioteca,
					biblioteca.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						nome = biblioteca.Nome,
						slug = biblioteca.Slug,
						icone = biblioteca.Icone,
						ativo = biblioteca.Ativo,
						todosLeem = command.TodosLeem,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return BibliotecaDto.FromEntity(biblioteca);
	}
}
