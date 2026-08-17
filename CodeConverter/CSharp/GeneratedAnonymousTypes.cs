using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ICSharpCode.CodeConverter.CSharp;

/// <summary>
/// Named classes generated to stand in for VB anonymous types that are mutated after
/// creation. C# anonymous types are immutable, so `ret.Message = msg` on one is CS0200.
///
/// Scoped to a single converted type: the generated classes are emitted as members of it,
/// so a given VB anonymous type maps to one class per containing type - two methods in the
/// same class that build the same shape share a class, but an identical shape in a
/// different class gets its own (it could not see the first one).
/// </summary>
internal class GeneratedAnonymousTypes
{
#pragma warning disable RS1024 // Compare symbols correctly - SymbolEqualityComparer.Default is what's wanted here, the analyzer just can't see it through the ctor
    private readonly Dictionary<INamedTypeSymbol, string> _namesByAnonymousType = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024
    private readonly List<MemberDeclarationSyntax> _declarations = new();

    public bool TryGetName(INamedTypeSymbol anonymousType, out string name) =>
        _namesByAnonymousType.TryGetValue(anonymousType, out name);

    public void Add(INamedTypeSymbol anonymousType, string name, MemberDeclarationSyntax declaration)
    {
        _namesByAnonymousType.Add(anonymousType, name);
        _declarations.Add(declaration);
    }

    public IReadOnlyCollection<MemberDeclarationSyntax> Declarations => _declarations;
}
