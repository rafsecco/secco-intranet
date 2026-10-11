using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regras de perfis do tenant administrado: nome, reservado, membros, permissões em texto e trilha.</summary>
public class PerfisDoTenantHandlerTests
{
	private static readonly Guid Tenant = Guid.NewGuid();

	private static (PerfisDoTenantHandler Handler, GestaoDeAcessoDoTenantFalsa Gestao, TrilhaDeAcessoFalsa Trilha) Criar()
	{
		var gestao = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras-aprovador", false, "pedido:aprovar")
			.ComPerfil("installation-operator", reservado: true)
			.ComUsuario(Guid.NewGuid(), "ana@compras.com", SituacaoDoUsuario.Ativo, perfis: "compras-aprovador")
			.ComPerfil("vazio");
		var trilha = new TrilhaDeAcessoFalsa();

		return (new PerfisDoTenantHandler(gestao, trilha), gestao, trilha);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("tem espaço")]
	[InlineData("-comeca-com-hifen")]
	public async Task Criar_NomeInvalido_NaoChamaAPorta(string? nome)
	{
		var (handler, gestao, _) = Criar();

		(await handler.CriarAsync(Tenant, nome)).IsFailure.Should().BeTrue();
		gestao.Chamadas.Should().NotContain(c => c.StartsWith("criarPerfil", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Criar_Reservado_Recusado()
	{
		var (handler, _, _) = Criar();

		(await handler.CriarAsync(Tenant, "installation-operator")).Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
	}

	[Fact]
	public async Task Criar_SemSufixoDeSetor_Aceito_ERegistraNaTrilha()
	{
		var (handler, gestao, trilha) = Criar();

		(await handler.CriarAsync(Tenant, " compras.aprovador ")).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().Contain($"criarPerfil:{Tenant}:compras.aprovador");
		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPerfilCriar);
		trilha.Registros[0].Metadata.Should().Contain(Tenant.ToString());
	}

	[Fact]
	public async Task Excluir_ComMembros_RecusadoSemChamarExclusao()
	{
		var (handler, gestao, _) = Criar();

		(await handler.ExcluirAsync(Tenant, "compras-aprovador")).Error.Should().Be(IntranetErrors.Acesso.PerfilComMembros);
		gestao.Chamadas.Should().NotContain(c => c.StartsWith("excluirPerfil", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Excluir_SemMembros_Exclui()
	{
		var (handler, gestao, _) = Criar();

		(await handler.ExcluirAsync(Tenant, "vazio")).IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Contain($"excluirPerfil:{Tenant}:vazio");
	}

	[Fact]
	public async Task Permissoes_LinhaInvalida_ApontaENaoGrava()
	{
		var (handler, gestao, _) = Criar();

		var definido = await handler.DefinirPermissoesAsync(Tenant, "compras-aprovador", "pedido:aprovar\npedido:aprovra mais\nPEDIDO:LER");

		definido.Error.Code.Should().Be("Intranet.Acesso.PermissoesInvalidas");
		definido.Error.Description.Should().Contain("pedido:aprovra mais").And.Contain("PEDIDO:LER");
		gestao.PermissoesDe("compras-aprovador").Should().Equal("pedido:aprovar");
	}

	[Fact]
	public async Task Permissoes_DuplicadasEVaziasDescartadas_TrilhaSoComContagem()
	{
		var (handler, gestao, trilha) = Criar();

		var definido = await handler.DefinirPermissoesAsync(Tenant, "compras-aprovador", "pedido:aprovar\r\n\r\n  pedido:ler  \npedido:aprovar\n");

		definido.IsSuccess.Should().BeTrue();
		gestao.PermissoesDe("compras-aprovador").Should().Equal("pedido:aprovar", "pedido:ler");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPermissoesEditar);
		registro.Metadata.Should().NotContain("pedido:ler", "a lista não vai para a trilha, só a contagem");
	}

	[Fact]
	public async Task Permissoes_SoLinhasEmBranco_GravaListaVazia()
	{
		var (handler, gestao, _) = Criar();

		(await handler.DefinirPermissoesAsync(Tenant, "compras-aprovador", "  \n\t\n")).IsSuccess.Should().BeTrue();
		gestao.PermissoesDe("compras-aprovador").Should().BeEmpty();
	}

	[Fact]
	public void Permissoes_AcimaDoLimite_Recusa()
	{
		var texto = string.Join('\n', Enumerable.Range(1, ListaDePermissoes.Maximo + 1).Select(i => $"recurso-{i}:ler"));

		ListaDePermissoes.Ler(texto).Error.Should().Be(IntranetErrors.Acesso.PermissoesDemais);
	}
}
