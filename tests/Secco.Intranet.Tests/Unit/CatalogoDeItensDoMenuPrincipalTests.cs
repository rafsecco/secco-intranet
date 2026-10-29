using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Extensibility;
using Secco.Intranet.Web.Navigation;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Catálogo dos itens extras do menu principal. Os itens de exemplo (classes aninhadas públicas)
/// também são vistos pelo host de integração, que carrega este assembly como application part;
/// por isso o gate de todos exige a role de teste e os testes olham chaves/tipos específicos,
/// nunca contagens.
/// </summary>
public class CatalogoDeItensDoMenuPrincipalTests
{
	private const string RoleDeTeste = "catalogo-teste";

	[Fact]
	public void Criar_AchaOItemNasApplicationParts()
	{
		var itens = Catalogo().Criar(Provider());

		itens.Should().Contain(i => i.Chave == "catalogo-simples");
		itens.Single(i => i.Chave == "catalogo-simples").Rotulo.Should().Be("Simples");
	}

	[Fact]
	public void Criar_IncluiOItemRegistradoPeloDi_SemDuplicarQuemTambemEstaNasParts()
	{
		var partsVazias = new ApplicationPartManager();
		var soRegistro = Catalogo(partsVazias, registros: [new RegistroDeItemDeMenuPrincipal(typeof(ItemSimples))]);
		soRegistro.Criar(Provider()).Select(i => i.Chave).Should().Equal("catalogo-simples");

		var ambos = Catalogo(registros: [new RegistroDeItemDeMenuPrincipal(typeof(ItemSimples))]);
		ambos.Criar(Provider()).Count(i => i.Chave == "catalogo-simples").Should().Be(1);
	}

	[Fact]
	public void Criar_InjetaServicoDoEscopo()
	{
		var servico = new ServicoDeTeste();
		var provider = new ServiceCollection().AddSingleton(servico).BuildServiceProvider();

		var item = Catalogo().Criar(provider).OfType<ItemComDependencia>().Single();

		item.Servico.Should().BeSameAs(servico);
	}

	[Fact]
	public void Criar_IgnoraChaveInvalida()
	{
		var tipos = Catalogo().Criar(Provider()).Select(i => i.GetType()).ToList();

		tipos.Should().NotContain(typeof(ItemChaveComEspaco));
		tipos.Should().NotContain(typeof(ItemChaveVazia));
		tipos.Should().NotContain(typeof(ItemChaveLonga));
		tipos.Should().Contain(typeof(ItemChaveNoLimite));
	}

	[Fact]
	public void Criar_IgnoraChaveReservada()
	{
		Catalogo().Criar(Provider()).Select(i => i.GetType()).Should().NotContain(typeof(ItemChaveReservada));
		ItemChaveReservada.ChaveUsada.Should().Be(CatalogoDeItensDoMenuPrincipal.ChaveMural);
		CatalogoDeItensDoMenuPrincipal.ChaveDiretorio.Should().Be("diretorio");
	}

	[Fact]
	public void Criar_IgnoraUrlNaoLocal()
	{
		var tipos = Catalogo().Criar(Provider()).Select(i => i.GetType()).ToList();

		tipos.Should().NotContain(typeof(ItemUrlAbsoluta));
		tipos.Should().NotContain(typeof(ItemUrlDeProtocoloRelativo));
		tipos.Should().NotContain(typeof(ItemUrlSemBarra));
	}

	[Fact]
	public void Criar_ChaveDuplicada_FicaOTipoDeMenorFullName()
	{
		var tipos = Catalogo().Criar(Provider()).Select(i => i.GetType()).ToList();

		tipos.Should().Contain(typeof(ItemDuplicadoA));
		tipos.Should().NotContain(typeof(ItemDuplicadoB));
	}

	[Fact]
	public void Criar_ItemInvalido_AvisaUmaVezSo()
	{
		var log = new LoggerCapturador<CatalogoDeItensDoMenuPrincipal>();
		var catalogo = Catalogo(log: log);

		catalogo.Criar(Provider());
		catalogo.Criar(Provider());

		log.Entradas.Where(e => e.Nivel == LogLevel.Warning && e.Mensagem.Contains(nameof(ItemChaveComEspaco), StringComparison.Ordinal))
			.Should().ContainSingle();
	}

	private static ServiceProvider Provider() => new ServiceCollection().AddSingleton<ServicoDeTeste>().BuildServiceProvider();

	private static CatalogoDeItensDoMenuPrincipal Catalogo(
		ApplicationPartManager? parts = null,
		LoggerCapturador<CatalogoDeItensDoMenuPrincipal>? log = null,
		RegistroDeItemDeMenuPrincipal[]? registros = null)
	{
		if (parts is null)
		{
			parts = new ApplicationPartManager();
			parts.ApplicationParts.Add(new AssemblyPart(typeof(CatalogoDeItensDoMenuPrincipalTests).Assembly));
		}

		return new CatalogoDeItensDoMenuPrincipal(parts, registros ?? [], log ?? new LoggerCapturador<CatalogoDeItensDoMenuPrincipal>());
	}

	/// <summary>Serviço de teste.</summary>
	public sealed class ServicoDeTeste;

	/// <summary>Base dos itens de exemplo: gate só para a role de teste, URL e chave configuráveis.</summary>
	public abstract class ItemBase : IItemDeMenuPrincipal
	{
		public abstract string Chave { get; }

		public virtual string Rotulo => "Simples";

		public string? Icone => null;

		public virtual string Url => "/catalogo-teste";

		public int Ordem => 0;

		public Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken) =>
			Task.FromResult(usuario.IsInRole(RoleDeTeste));
	}

	public sealed class ItemSimples : ItemBase
	{
		public override string Chave => "catalogo-simples";
	}

	public sealed class ItemComDependencia(ServicoDeTeste servico) : ItemBase
	{
		public ServicoDeTeste Servico { get; } = servico;

		public override string Chave => "catalogo-dependencia";
	}

	public sealed class ItemChaveComEspaco : ItemBase
	{
		public override string Chave => "Com Espaco";
	}

	public sealed class ItemChaveVazia : ItemBase
	{
		public override string Chave => "";
	}

	public sealed class ItemChaveLonga : ItemBase
	{
		public override string Chave => new('a', 65);
	}

	public sealed class ItemChaveNoLimite : ItemBase
	{
		public override string Chave => new('b', 64);
	}

	public sealed class ItemChaveReservada : ItemBase
	{
		public const string ChaveUsada = "mural";

		public override string Chave => ChaveUsada;
	}

	public sealed class ItemUrlAbsoluta : ItemBase
	{
		public override string Chave => "catalogo-url-absoluta";

		public override string Url => "https://x";
	}

	public sealed class ItemUrlDeProtocoloRelativo : ItemBase
	{
		public override string Chave => "catalogo-url-protocolo";

		public override string Url => "//x";
	}

	public sealed class ItemUrlSemBarra : ItemBase
	{
		public override string Chave => "catalogo-url-sem-barra";

		public override string Url => "x";
	}

	public sealed class ItemDuplicadoA : ItemBase
	{
		public override string Chave => "catalogo-duplicado";
	}

	public sealed class ItemDuplicadoB : ItemBase
	{
		public override string Chave => "catalogo-duplicado";
	}
}
