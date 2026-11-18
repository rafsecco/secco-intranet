using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Inventario;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Domain.Vencimentos;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.Intranet.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados sobre o banco do tenant atual (ADR-0005) — a connection string vem
/// do <c>ITenantConnectionFactory</c> a cada requisição. Herda de <see cref="SeccoDbContext"/>:
/// nomenclatura da ADR-0017 aplicada por convention — ninguém digita nomes de coluna.
/// </summary>
public sealed class IntranetDbContext(DbContextOptions<IntranetDbContext> options)
	: SeccoDbContext(options)
{
	/// <summary>Setores/departamentos (tabela <c>tb_setores</c>).</summary>
	public DbSet<Setor> Setores => Set<Setor>();

	/// <summary>Bibliotecas de documentos, sem setor dono (tabela <c>tb_bibliotecas</c>).</summary>
	public DbSet<Biblioteca> Bibliotecas => Set<Biblioteca>();

	/// <summary>Documentos publicados por setor ou biblioteca (tabela <c>tb_documentos</c>).</summary>
	public DbSet<Documento> Documentos => Set<Documento>();

	/// <summary>Publicações do mural (tabela <c>tb_publicacoes</c>).</summary>
	public DbSet<Publicacao> Publicacoes => Set<Publicacao>();

	/// <summary>Imagens inseridas no texto das publicações (tabela <c>tb_imagens_no_texto</c>).</summary>
	public DbSet<ImagemNoTexto> ImagensNoTexto => Set<ImagemNoTexto>();

	/// <summary>Itens de inventário, sem setor dono (tabela <c>tb_itens_inventario</c>).</summary>
	public DbSet<ItemInventario> ItensInventario => Set<ItemInventario>();

	/// <summary>Perfis complementares de colaborador (tabela <c>tb_perfis_colaboradores</c>).</summary>
	public DbSet<PerfilColaborador> PerfisColaboradores => Set<PerfilColaborador>();

	/// <summary>Árvore de itens de menu da página de cada setor.</summary>
	public DbSet<ItemMenu> ItensMenu => Set<ItemMenu>();

	/// <summary>Posições salvas do menu principal por tenant (tabela <c>tb_posicoes_menu</c>).</summary>
	public DbSet<PosicaoMenu> PosicoesMenu => Set<PosicaoMenu>();

	/// <summary>Itens com data de vencimento por setor (tabela <c>tb_itens_vencimento</c>).</summary>
	public DbSet<ItemVencimento> ItensVencimento => Set<ItemVencimento>();

	/// <summary>Histórico de renovações dos itens de vencimento.</summary>
	public DbSet<RenovacaoDeVencimento> RenovacoesDeVencimento => Set<RenovacaoDeVencimento>();

	/// <summary>Marcos de aviso já tratados por item e ciclo.</summary>
	public DbSet<AvisoDeVencimentoEnviado> AvisosDeVencimentoEnviados => Set<AvisoDeVencimentoEnviado>();

	/// <summary>Salas de reunião reserváveis (tabela <c>tb_salas</c>).</summary>
	public DbSet<Sala> Salas => Set<Sala>();

	/// <summary>Reservas de sala (tabela <c>tb_reservas</c>).</summary>
	public DbSet<Reserva> Reservas => Set<Reserva>();

	/// <summary>Convidados das reservas.</summary>
	public DbSet<ConvidadoDaReserva> ConvidadosDasReservas => Set<ConvidadoDaReserva>();

	/// <summary>Eventos da agenda externa que ainda precisam ser excluídos.</summary>
	public DbSet<ExclusaoDeEventoPendente> ExclusoesDeEventoPendentes => Set<ExclusaoDeEventoPendente>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntranetDbContext).Assembly);
	}
}
