using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Adaptador do acesso do tenant administrado: tenant explícito, mapeamento do cadastro e dos 409.</summary>
public class SecureGateGestaoDeAcessoDoTenantTests
{
	private static readonly Guid Alvo = Guid.NewGuid();

	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public List<string> Chamadas { get; } = [];

		public CreateUserRequest? Criado { get; private set; }

		public Exception? Falha { get; set; }

		private Task Registrar(string chamada)
		{
			Chamadas.Add(chamada);

			return Falha is null ? Task.CompletedTask : Task.FromException(Falha);
		}

		public override async Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoles:{tenantId}");

			return [new RoleDto { Name = "compras-aprovador", Permissions = ["pedido:aprovar"] }];
		}

		public override async Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"CreateUser:{tenantId}");
			Criado = body;

			return new UserDto { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Email = body.Email };
		}

		public override async Task<UserDetailDto> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
		{
			await Registrar($"GetUser:{tenantId}:{userId}");

			return new UserDetailDto
			{
				Id = userId, Email = "ana@compras.com", Status = "Active", HasPassword = false, LocalLoginEnabled = true, Membership = Vinculo,
				DisplayName = "Ana", Roles = ["compras-aprovador"],
			};
		}

		public override async Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken) =>
			await Registrar($"DeleteRole:{tenantId}:{role}");

		public ICollection<ProductPermissionCatalogDto> Catalogo { get; set; } = [];

		public string? Vinculo { get; set; } = "Owner";

		public override async Task<TenantMemberDto> AddTenantMemberAsync(Guid tenantId, AddTenantMemberRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"AddTenantMember:{tenantId}:{body.Email}");

			return new TenantMemberDto { UserId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Email = body.Email };
		}

		public override async Task SuspendTenantMemberAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			await Registrar($"SuspendTenantMember:{tenantId}:{userId}");

		public RoleDetailDto? Papel { get; set; }

		public override async Task<ICollection<ProductPermissionCatalogDto>> ListPermissionCatalogAsync(CancellationToken cancellationToken)
		{
			await Registrar("ListPermissionCatalog");

			return Catalogo;
		}

		public override async Task<RoleDetailDto> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken)
		{
			await Registrar($"GetRole:{tenantId}:{role}");

			return Papel!;
		}

		public override async Task AddUserRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken) =>
			await Registrar($"AddUserRole:{tenantId}:{userId}:{role}");

		public override async Task SetRolePermissionsAsync(
			Guid tenantId, string role, SetRolePermissionsRequest body, CancellationToken cancellationToken) =>
			await Registrar($"SetRolePermissions:{tenantId}:{role}");

		public override async Task ResendUserInviteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			await Registrar($"ResendUserInvite:{tenantId}:{userId}");
	}

	private static (SecureGateGestaoDeAcessoDoTenant Gestao, ClientFalso Client) Criar()
	{
		var client = new ClientFalso();

		return (new SecureGateGestaoDeAcessoDoTenant(client, NullLogger<SecureGateGestaoDeAcessoDoTenant>.Instance), client);
	}

	private static ApiException Api(int status, string corpo = "") =>
		new("falha", status, corpo, new Dictionary<string, IEnumerable<string>>(), null);

	[Fact]
	public async Task ListarPerfis_UsaOTenantRecebido()
	{
		var (gestao, client) = Criar();

		var perfis = await gestao.ListarPerfisAsync(Alvo);

		client.Chamadas.Should().Equal($"ListRoles:{Alvo}");
		perfis.Value.Should().ContainSingle().Which.Should().Be(new PerfilDoTenantDto("compras-aprovador", 1, false));
	}

	[Fact]
	public async Task Cadastrar_MontaOPedidoEDevolveOId()
	{
		var (gestao, client) = Criar();

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", "Ana", ["compras-aprovador"], EntraPorSenha: false));

		id.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		client.Chamadas.Should().Equal($"CreateUser:{Alvo}");
		client.Criado!.Email.Should().Be("ana@compras.com");
		client.Criado.LocalLogin.Should().BeFalse();
		client.Criado.DisplayName.Should().Be("Ana");
		client.Criado.Roles.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task Cadastrar_409_ViraEmailJaCadastrado()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409, "{\"code\":\"SecureGate.User.AlreadyExists\"}");

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", null, [], true));

		id.Error.Should().Be(IntranetErrors.Acesso.EmailJaCadastrado);
	}

	[Fact]
	public async Task Cadastrar_400_ViraCadastroRecusado()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(400);

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", null, [], true));

		id.Error.Should().Be(IntranetErrors.Acesso.CadastroRecusado);
	}

	[Fact]
	public async Task ObterUsuario_TrazSenhaEModoDeEntrada()
	{
		var (gestao, _) = Criar();
		var usuario = Guid.NewGuid();

		var lido = await gestao.ObterUsuarioAsync(Alvo, usuario);

		lido.Value.TemSenha.Should().BeFalse();
		lido.Value.EntraPorSenha.Should().BeTrue();
		lido.Value.Nome.Should().Be("Ana");
	}

	[Theory]
	[InlineData("{\"code\":\"SecureGate.Role.UsedByClients\"}", "Intranet.Acesso.PerfilUsadoPorClient")]
	[InlineData("{\"code\":\"SecureGate.Role.HasMembers\"}", "Intranet.Acesso.PerfilComMembros")]
	public async Task ExcluirPerfil_409_DistingueOMotivo(string corpo, string codigoEsperado)
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409, corpo);

		(await gestao.ExcluirPerfilAsync(Alvo, "x")).Error.Code.Should().Be(codigoEsperado);
	}

	[Fact]
	public async Task DefinirPermissoes_ForaDoCatalogo_ViraErroProprio_NaoIndisponivel()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(400, "{\"title\":\"SecureGate.Role.PermissionsNotInCatalog\",\"status\":400}");

		(await gestao.DefinirPermissoesDoPerfilAsync(Alvo, "p", ["pedido:aprovar"]))
			.Error.Should().Be(IntranetErrors.Acesso.PermissoesForaDoCatalogo);
	}

	[Fact]
	public async Task ReenviarConvite_409_ViraConviteNaoCabe()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409);

		(await gestao.ReenviarConviteAsync(Alvo, Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
	}

	[Fact]
	public async Task ListarCatalogo_AgrupaPorProduto_ComADescricao()
	{
		var (gestao, client) = Criar();
		client.Catalogo =
		[
			new ProductPermissionCatalogDto
			{
				Product = "compras",
				Permissions =
				[
					new PermissionDefinitionDto { Name = "pedido:aprovar", Description = "Aprovar pedidos" },
					new PermissionDefinitionDto { Name = "filial-{id}:read", Description = "Ler uma filial", IsFamily = true },
				],
			},
		];

		var catalogo = await gestao.ListarCatalogoDePermissoesAsync();

		catalogo.Value.Should().ContainSingle().Which.Should().BeEquivalentTo(
			new ProdutoDoCatalogoDto(
				"compras",
				[new PermissaoDoCatalogoDto("pedido:aprovar", "Aprovar pedidos"), new PermissaoDoCatalogoDto("filial-{id}:read", "Ler uma filial", Familia: true)]));
	}

	[Fact]
	public async Task ObterPerfil_TrazAsPermissoesForaDoCatalogo()
	{
		var (gestao, client) = Criar();
		client.Papel = new RoleDetailDto
		{
			Name = "aprovador", Permissions = ["pedido:aprovar", "pedido:antigo"], UnknownPermissions = ["pedido:antigo"],
		};

		var perfil = await gestao.ObterPerfilAsync(Alvo, "aprovador");

		perfil.Value.PermissoesForaDoCatalogo.Should().Equal("pedido:antigo");
	}

	[Fact]
	public async Task AtribuirPerfil_PessoaQueNaoEMembro_ViraErroProprio()
	{
		var (gestao, client) = Criar();
		client.Falha = new ApiException<ProblemDetails>(
			"erro", 409, null!, new Dictionary<string, IEnumerable<string>>(),
			new ProblemDetails { Title = "SecureGate.Member.NotMember", Status = 409 }, null!);

		(await gestao.AtribuirPerfilAsync(Alvo, Guid.NewGuid(), "aprovador"))
			.Error.Should().Be(IntranetErrors.Acesso.PessoaNaoEMembro);
	}

	private static ApiException<ProblemDetails> Problema(int status, string codigo) =>
		new("erro", status, null!, new Dictionary<string, IEnumerable<string>>(), new ProblemDetails { Title = codigo, Status = status }, null!);

	[Theory]
	[InlineData("Owner", VinculoNoTenant.Dono)]
	[InlineData("Member", VinculoNoTenant.Membro)]
	[InlineData("Suspended", VinculoNoTenant.Suspenso)]
	[InlineData(null, VinculoNoTenant.Dono)]
	public async Task ObterUsuario_TraduzOVinculo(string? membership, VinculoNoTenant esperado)
	{
		var (gestao, client) = Criar();
		client.Vinculo = membership;

		(await gestao.ObterUsuarioAsync(Alvo, Guid.NewGuid())).Value.Vinculo.Should().Be(esperado);
	}

	[Fact]
	public async Task IncluirMembro_UsaOTenantRecebido_EDevolveOId()
	{
		var (gestao, client) = Criar();

		var incluido = await gestao.IncluirMembroAsync(Alvo, "eva@intranet.com");

		incluido.Value.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
		client.Chamadas.Should().Equal($"AddTenantMember:{Alvo}:eva@intranet.com");
	}

	[Fact]
	public async Task IncluirMembro_JaMembro_ViraErroProprio()
	{
		var (gestao, client) = Criar();
		client.Falha = Problema(409, "SecureGate.Member.AlreadyMember");

		(await gestao.IncluirMembroAsync(Alvo, "eva@intranet.com")).Error.Should().Be(IntranetErrors.Acesso.JaEMembro);
	}

	[Fact]
	public async Task SuspenderVinculo_DoDono_ViraErroProprio()
	{
		var (gestao, client) = Criar();
		client.Falha = Problema(409, "SecureGate.Member.OwnerMembership");

		(await gestao.SuspenderVinculoAsync(Alvo, Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.VinculoDoDono);
	}
}
