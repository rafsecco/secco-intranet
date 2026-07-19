using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Setores;

/// <summary>
/// Setor/departamento da instituição (ex: Financeiro, RH, Infraestrutura). É o eixo
/// organizacional da Intranet: documentos, processos e outros recursos são habilitados
/// por setor, e o acesso de cada usuário é resolvido via as Roles tenant-scoped
/// <c>{slug}-admin</c> e <c>{slug}-user</c> criadas no Secco.SecureGate quando o setor
/// é cadastrado (ver Secco.Intranet.Infrastructure.SecureGate).
/// </summary>
public sealed class Setor : BaseEntity
{
	private Setor()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
		Slug = string.Empty;
	}

	/// <summary>Cria um setor.</summary>
	/// <param name="nome">Nome de exibição. Obrigatório.</param>
	/// <param name="slug">Identificador curto usado em rotas e nas Roles do SecureGate. Obrigatório.</param>
	/// <param name="fixo">
	/// Quando <c>true</c>, o setor não pode ser desabilitado nem excluído pela tela de
	/// administração (caso do setor de Infraestrutura, dono nato do recurso de Inventário).
	/// </param>
	/// <exception cref="DomainInvariantException">Se nome ou slug forem nulos ou vazios.</exception>
	public Setor(string nome, string slug, bool fixo = false)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um setor exige nome não vazio.");
		}

		if (string.IsNullOrWhiteSpace(slug))
		{
			throw new DomainInvariantException("Um setor exige slug não vazio.");
		}

		Nome = nome;
		Slug = slug.Trim().ToLowerInvariant();
		Fixo = fixo;
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Nome de exibição (coluna <c>ds_nome</c>).</summary>
	public string Nome { get; private set; }

	/// <summary>
	/// Identificador curto, único por tenant — base do nome das Roles no SecureGate
	/// (<c>{slug}-admin</c> / <c>{slug}-user</c>) e das rotas (coluna <c>ds_slug</c>).
	/// </summary>
	public string Slug { get; private set; }

	/// <summary>
	/// Setor fixo do sistema (ex: Infraestrutura) — não pode ser desabilitado nem excluído
	/// (coluna <c>fl_fixo</c>).
	/// </summary>
	public bool Fixo { get; private set; }

	/// <summary>Setor ativo (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Renomeia o setor.</summary>
	/// <param name="nome">Novo nome de exibição. Obrigatório.</param>
	/// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
	public void Renomear(string nome)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um setor exige nome não vazio.");
		}

		Nome = nome;
	}

	/// <summary>
	/// Desativa o setor (deixa de aparecer no menu e nas telas de vínculo de usuário).
	/// </summary>
	/// <exception cref="DomainInvariantException">Se o setor for fixo.</exception>
	public void Desativar()
	{
		if (Fixo)
		{
			throw new DomainInvariantException("Um setor fixo não pode ser desativado.");
		}

		Ativo = false;
	}

	/// <summary>Reativa o setor.</summary>
	public void Ativar() => Ativo = true;
}
