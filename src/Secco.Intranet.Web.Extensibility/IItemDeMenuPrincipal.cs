using System.Security.Claims;

namespace Secco.Intranet.Web.Extensibility;

/// <summary>
/// Item fixo extra do grupo principal do menu, oferecido por um projeto externo (RCL) sem editar
/// o core. O core descobre as implementações públicas e concretas nas application parts do MVC
/// (ou as registradas por <c>AddItemDeMenuPrincipal&lt;T&gt;()</c>) e as instancia a cada
/// requisição, então o construtor pode receber serviços scoped. O item é uma folha: sem filhos.
/// Item que viola uma regra abaixo, ou cujo gate lança exceção, fica fora do menu com um aviso
/// no log; nunca derruba a página.
/// </summary>
public interface IItemDeMenuPrincipal
{
	/// <summary>
	/// Identidade estável do item; é o que o banco guarda para lembrar a posição escolhida pelo
	/// admin. De 1 a 64 caracteres, só <c>[a-z0-9-]</c>. <c>mural</c> e <c>diretorio</c> são
	/// reservadas aos itens fixos do core. Com duas implementações na mesma chave, vale a de menor
	/// nome completo de tipo (ordinal) e a outra é ignorada.
	/// </summary>
	string Chave { get; }

	/// <summary>Texto exibido no menu; obrigatório (nulo, vazio ou só espaços deixa o item de fora).</summary>
	string Rotulo { get; }

	/// <summary>Classe do Bootstrap Icons (<c>bi-*</c>); nulo para item sem ícone.</summary>
	string? Icone { get; }

	/// <summary>Caminho local do destino: começa com <c>/</c> e não com <c>//</c>.</summary>
	string Url { get; }

	/// <summary>Posição padrão entre os extras (depois, por <see cref="Rotulo"/>), até o admin reordenar.</summary>
	int Ordem { get; }

	/// <summary>
	/// Gate de visibilidade do item para o usuário da requisição. Não é chamado no modo aberto de
	/// desenvolvimento, onde todos os itens aparecem. Exceção aqui conta como invisível.
	/// </summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="cancellationToken">Cancelamento da requisição.</param>
	/// <returns><see langword="true"/> se o usuário deve ver o item.</returns>
	Task<bool> VisivelParaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken);
}
