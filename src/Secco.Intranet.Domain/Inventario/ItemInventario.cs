using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Inventario;

/// <summary>Situação de um item de inventário.</summary>
public enum StatusDoItem
{
	/// <summary>Livre para ser atribuído.</summary>
	Disponivel = 0,

	/// <summary>Em uso por alguém (<see cref="ItemInventario.AtribuidoAUsuarioId"/>).</summary>
	EmUso = 1,

	/// <summary>Em manutenção — não pode ser atribuído até voltar.</summary>
	EmManutencao = 2,

	/// <summary>Baixado. Terminal: nenhuma outra transição é aceita.</summary>
	Baixado = 3,
}

/// <summary>
/// Item de inventário. Não pertence a nenhum Setor — <see cref="SetorId"/> é informativo
/// ("este item está com o Financeiro"), não dá autorização; quem administra é a Role
/// <c>inventario-admin</c> (ou <c>intranet-admin</c>), ver <c>AcessoAdministrativo</c>.
/// Decisão revista em 2026-09-12: a ADR-0001 original cogitava o setor Infraestrutura como
/// "dono nato" deste recurso.
/// </summary>
public sealed class ItemInventario : BaseEntity
{
	private ItemInventario()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
	}

	/// <summary>Cria um item de inventário, sempre nascendo <see cref="StatusDoItem.Disponivel"/>.</summary>
	/// <param name="nome">Nome de exibição. Obrigatório.</param>
	/// <param name="descricao">Descrição livre. Opcional.</param>
	/// <param name="categoria">Categoria livre. Opcional — sem catálogo fechado (YAGNI).</param>
	/// <param name="codigoPatrimonio">Código de patrimônio livre, sem unicidade. Opcional.</param>
	/// <param name="setorId">Setor onde o item está, informativo. Opcional.</param>
	/// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
	public ItemInventario(string nome, string? descricao, string? categoria, string? codigoPatrimonio, Guid? setorId)
	{
		ValidarNome(nome);

		Nome = nome;
		Descricao = descricao;
		Categoria = categoria;
		CodigoPatrimonio = codigoPatrimonio;
		SetorId = setorId;
		Status = StatusDoItem.Disponivel;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Nome de exibição (coluna <c>ds_nome</c>).</summary>
	public string Nome { get; private set; }

	/// <summary>Descrição livre (coluna <c>ds_descricao</c>).</summary>
	public string? Descricao { get; private set; }

	/// <summary>Categoria livre (coluna <c>ds_categoria</c>).</summary>
	public string? Categoria { get; private set; }

	/// <summary>Código de patrimônio, sem unicidade forçada (coluna <c>ds_codigo_patrimonio</c>).</summary>
	public string? CodigoPatrimonio { get; private set; }

	/// <summary>Setor onde o item está — informativo, não dá autorização (coluna <c>id_fk_setor</c>).</summary>
	public Guid? SetorId { get; private set; }

	/// <summary>Situação atual (coluna <c>ie_status</c>).</summary>
	public StatusDoItem Status { get; private set; }

	/// <summary>
	/// Usuário do SecureGate a quem o item está atribuído (coluna <c>atribuido_a_usuario_id</c>
	/// — sem prefixo <c>id_fk_</c>: não é uma FK reconhecida pelo EF, porque não há tabela
	/// local de usuário para relacionar; identidade vive só no SecureGate, ADR-0006).
	/// </summary>
	public Guid? AtribuidoAUsuarioId { get; private set; }

	/// <summary>
	/// Identificador em cache de quem tem o item — e-mail, não nome: o SecureGate não guarda
	/// nome de exibição (coluna <c>ds_atribuido_a_nome</c>). Mesmo padrão de
	/// <c>Documento.CriadoPor</c>, para não duplicar identidade (ADR-0006).
	/// </summary>
	public string? AtribuidoANome { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Altera os campos descritivos. Não mexe em status nem atribuição.</summary>
	/// <exception cref="DomainInvariantException">Nome vazio, ou item já <see cref="StatusDoItem.Baixado"/>.</exception>
	public void Editar(string nome, string? descricao, string? categoria, string? codigoPatrimonio, Guid? setorId)
	{
		GarantirNaoBaixado();
		ValidarNome(nome);

		Nome = nome;
		Descricao = descricao;
		Categoria = categoria;
		CodigoPatrimonio = codigoPatrimonio;
		SetorId = setorId;
	}

	/// <summary>Atribui o item a um usuário. Válido a partir de Disponível ou Em uso (reatribuição).</summary>
	/// <exception cref="DomainInvariantException">Usuário vazio, item Baixado, ou item Em manutenção.</exception>
	public void Atribuir(Guid usuarioId, string? usuarioNome)
	{
		GarantirNaoBaixado();

		if (Status is not (StatusDoItem.Disponivel or StatusDoItem.EmUso))
		{
			throw new DomainInvariantException("Atribuir só é válido a partir de Disponível ou Em uso.");
		}

		if (usuarioId == Guid.Empty)
		{
			throw new DomainInvariantException("Atribuir exige um usuário válido.");
		}

		AtribuidoAUsuarioId = usuarioId;
		AtribuidoANome = usuarioNome;
		Status = StatusDoItem.EmUso;
	}

	/// <summary>Libera o item. Só válido a partir de Em uso.</summary>
	/// <exception cref="DomainInvariantException">Item não está Em uso, ou está Baixado.</exception>
	public void Desatribuir()
	{
		GarantirNaoBaixado();

		if (Status != StatusDoItem.EmUso)
		{
			throw new DomainInvariantException("Só um item Em uso pode ser desatribuído.");
		}

		AtribuidoAUsuarioId = null;
		AtribuidoANome = null;
		Status = StatusDoItem.Disponivel;
	}

	/// <summary>Envia para manutenção. Não mexe em quem está atribuído — pode voltar para a mesma pessoa.</summary>
	/// <exception cref="DomainInvariantException">Item Baixado.</exception>
	public void EnviarParaManutencao()
	{
		GarantirNaoBaixado();

		Status = StatusDoItem.EmManutencao;
	}

	/// <summary>Volta da manutenção — Em uso se havia atribuição, Disponível caso contrário.</summary>
	/// <exception cref="DomainInvariantException">Item não está Em manutenção.</exception>
	public void VoltarDaManutencao()
	{
		GarantirNaoBaixado();

		if (Status != StatusDoItem.EmManutencao)
		{
			throw new DomainInvariantException("Só um item Em manutenção pode voltar da manutenção.");
		}

		Status = AtribuidoAUsuarioId is null ? StatusDoItem.Disponivel : StatusDoItem.EmUso;
	}

	/// <summary>Dá baixa no item. Terminal: nenhuma outra transição é aceita depois.</summary>
	/// <exception cref="DomainInvariantException">Item já Baixado.</exception>
	public void Baixar()
	{
		GarantirNaoBaixado();

		Status = StatusDoItem.Baixado;
	}

	private void GarantirNaoBaixado()
	{
		if (Status == StatusDoItem.Baixado)
		{
			throw new DomainInvariantException("Um item baixado não aceita mais alterações.");
		}
	}

	private static void ValidarNome(string nome)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um item de inventário exige nome não vazio.");
		}
	}
}
