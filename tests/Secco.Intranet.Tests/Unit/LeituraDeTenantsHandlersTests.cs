using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeituraDeTenantsHandlersTests
{
	private static readonly Guid Compras = Guid.NewGuid();
	private static readonly Guid Erp = Guid.NewGuid();

	[Fact]
	public async Task Listar_SoOsDoCadastro_ComRecursosLigados()
	{
		var gestao = new GestaoDeTenantsFalsa()
			.ComTenant(Compras, "compras", true, "logstream")
			.ComTenant(Erp, "erp");
		var cadastro = new TenantsAdministradosFalso().Com(Compras, "Sistema de compras", secureGate: true);

		var resultado = await new ListarTenantsAdministradosHandler(cadastro, gestao).HandleAsync();

		resultado.IsSuccess.Should().BeTrue();
		var linha = resultado.Value.Should().ContainSingle().Subject;
		linha.TenantId.Should().Be(Compras);
		linha.Slug.Should().Be("compras");
		linha.EncontradoNaPlataforma.Should().BeTrue();
		linha.RecursosLigados.Should().Equal(RecursoDaPlataforma.SecureGate, RecursoDaPlataforma.LogStream);
	}

	[Fact]
	public async Task Listar_TenantSumiuDaPlataforma_AindaApareceMarcado()
	{
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ListarTenantsAdministradosHandler(cadastro, new GestaoDeTenantsFalsa()).HandleAsync();

		var linha = resultado.Value.Should().ContainSingle().Subject;
		linha.EncontradoNaPlataforma.Should().BeFalse();
		linha.Ativo.Should().BeFalse();
	}

	[Fact]
	public async Task Listar_SecureGateFora_Falha()
	{
		var gestao = new GestaoDeTenantsFalsa { FalharCom = IntranetErrors.Tenants.Indisponivel };
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ListarTenantsAdministradosHandler(cadastro, gestao).HandleAsync();

		resultado.Error.Should().Be(IntranetErrors.Tenants.Indisponivel);
	}

	[Fact]
	public async Task Obter_ForaDoCadastro_NaoEncontrado_SemChamarAPlataforma()
	{
		var gestao = new GestaoDeTenantsFalsa().ComTenant(Erp, "erp");

		var resultado = await new ObterTenantAdministradoHandler(new TenantsAdministradosFalso(), gestao).HandleAsync(Erp);

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		gestao.Chamadas.Should().BeEmpty("tenant fora do cadastro não existe para a área");
	}

	[Fact]
	public async Task Obter_MontaOsTresRecursos()
	{
		var gestao = new GestaoDeTenantsFalsa().ComTenant(Compras, "compras", true, "logstream", "notificationhub");
		gestao.BancosSemResponder[(Compras, "notificationhub")] = "login-failed";
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ObterTenantAdministradoHandler(cadastro, gestao).HandleAsync(Compras);

		resultado.Value.Recursos.Should().Equal(
			new RecursoDoTenantDto(RecursoDaPlataforma.SecureGate, SituacaoDoRecurso.NaoLigado, null),
			new RecursoDoTenantDto(RecursoDaPlataforma.LogStream, SituacaoDoRecurso.Ligado, null),
			new RecursoDoTenantDto(RecursoDaPlataforma.NotificationHub, SituacaoDoRecurso.LigadoSemResponder, "login-failed"));
	}

	[Fact]
	public async Task Adotaveis_ExcluiProtegidosEJaAdministrados()
	{
		var instalacao = Guid.NewGuid();
		var livre = Guid.NewGuid();
		var gestao = new GestaoDeTenantsFalsa()
			.ComTenant(instalacao, "instalacao")
			.ComTenant(Compras, "compras")
			.ComTenant(livre, "erp");
		var cadastro = new TenantsAdministradosFalso().Com(Compras);

		var resultado = await new ListarTenantsAdotaveisHandler(gestao, cadastro, new TenantsProtegidosFixos(instalacao))
			.HandleAsync();

		resultado.Value.Select(t => t.Id).Should().Equal(livre);
	}

	[Fact]
	public async Task Verificar_SoOsDoCadastro()
	{
		var handler = new VerificarTenantAdministradoHandler(new TenantsAdministradosFalso().Com(Compras));

		(await handler.HandleAsync(Compras)).Should().BeTrue();
		(await handler.HandleAsync(Erp)).Should().BeFalse();
		(await handler.HandleAsync(Guid.Empty)).Should().BeFalse();
	}
}
