using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ICSharpCode.CodeConverter.CSharp;

internal class LambdaConverter
{
    private readonly SemanticModel _semanticModel;
    private readonly Solution _solution;

    public LambdaConverter(CommonConversions commonConversions, SemanticModel semanticModel)
    {
        CommonConversions = commonConversions;
        _semanticModel = semanticModel;
        _solution = CommonConversions.Document.Project.Solution;
    }

    public CommonConversions CommonConversions { get; }

    public async Task<CSharpSyntaxNode> ConvertAsync(VBSyntax.LambdaExpressionSyntax vbNode,
        ParameterListSyntax param, IReadOnlyCollection<StatementSyntax> convertedStatements)
    {
        BlockSyntax block = null;
        ExpressionSyntax expressionBody = null;
        ArrowExpressionClauseSyntax arrow = null;
        if (!convertedStatements.TryUnpackSingleStatement(out StatementSyntax singleStatement)) {
            convertedStatements = convertedStatements.Select(l => l.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)).ToList();
            block = SyntaxFactory.Block(convertedStatements);
        } else if (singleStatement.TryUnpackSingleExpressionFromStatement(out expressionBody)) {
            arrow = SyntaxFactory.ArrowExpressionClause(expressionBody);
        } else {
            block = SyntaxFactory.Block(singleStatement);
        }

        var functionStatement = await ConvertToFunctionDeclarationOrNullAsync(vbNode, param, block, arrow);
        if (functionStatement != null) {
            return functionStatement;
        }

        var body = (CSharpSyntaxNode)block ?? expressionBody;
        var isAnonAsync = _semanticModel.GetOperation(vbNode) is IAnonymousFunctionOperation a && a.Symbol.IsAsync;

        var asyncKeyword = SyntaxFactory.Token(SyntaxKind.AsyncKeyword);
        LambdaExpressionSyntax lambda;
        if (param.Parameters.Count == 1 && param.Parameters.Single().Type == null) {
            var l = SyntaxFactory.SimpleLambdaExpression(param.Parameters[0], body);
            lambda = isAnonAsync ? l.WithAsyncKeyword(asyncKeyword) : l;
        } else {
            var l = SyntaxFactory.ParenthesizedLambdaExpression(param, body);
            lambda = isAnonAsync ? l.WithAsyncKeyword(asyncKeyword) : l;
        }

        return lambda;
    }


    private async Task<CSharpSyntaxNode> ConvertToFunctionDeclarationOrNullAsync(VBSyntax.LambdaExpressionSyntax vbNode,
        ParameterListSyntax param, BlockSyntax block,
        ArrowExpressionClauseSyntax arrow)
    {
        if (!(_semanticModel.GetOperation(vbNode) is IAnonymousFunctionOperation anonFuncOp) || anonFuncOp.GetParentIgnoringConversions() is IDelegateCreationOperation dco && !dco.IsImplicit) {
            return null;
        }

        var potentialAncestorDeclarationOperation = anonFuncOp.GetParentIgnoringConversions().GetParentIgnoringConversions();
        // Could do: See if we can improve upon returning "object" for pretty much everything (which is what the symbols say)
        // I believe that in general, special VB functions such as MultiplyObject are designed to work the same as integer when given two integers for example.
        // If all callers currently pass an integer, perhaps it'd be more idiomatic in C# to specify "int", than to have Operators
        var paramsWithTypes = anonFuncOp.Symbol.Parameters.Select(p => (ParameterSyntax) CommonConversions.CsSyntaxGenerator.ParameterDeclaration(p));

        var paramListWithTypes = param.WithParameters(SyntaxFactory.SeparatedList(paramsWithTypes));
        if (potentialAncestorDeclarationOperation is IFieldInitializerOperation fieldInit) {
            var fieldSymbol = fieldInit.InitializedFields.Single();
            if (fieldSymbol.GetResultantVisibility() != SymbolVisibility.Public && !fieldSymbol.Type.IsDelegateReferencableByName() && await _solution.IsNeverWrittenAsync(fieldSymbol)) {
                return CreateMethodDeclaration(anonFuncOp, fieldSymbol, block, arrow);
            }
        }

        if (potentialAncestorDeclarationOperation is IVariableInitializerOperation vio) {
            if (vio.GetParentIgnoringConversions().GetParentIgnoringConversions() is IVariableDeclarationGroupOperation go) {
                potentialAncestorDeclarationOperation = go.Declarations.First(d => d.Syntax.FullSpan.Contains(vbNode.FullSpan));
            } else {
                potentialAncestorDeclarationOperation = vio.Parent;
            }

            if (potentialAncestorDeclarationOperation is IVariableDeclarationOperation variableDeclaration) {
                var variableDeclaratorOperation = variableDeclaration.Declarators.Single();
                if (!variableDeclaratorOperation.Symbol.Type.IsDelegateReferencableByName() &&
                    await _solution.IsNeverWrittenAsync(variableDeclaratorOperation.Symbol) &&
                    !IsReferencedInsideExpressionTree(variableDeclaratorOperation.Symbol, variableDeclaratorOperation.Syntax)) {
                    //Should do: Check no (other) write usages exist: SymbolFinder.FindReferencesAsync + checking if they're an assignment LHS or out parameter
                    return CreateLocalFunction(anonFuncOp, variableDeclaratorOperation, paramListWithTypes, block,
                        arrow);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A lambda-holding variable converted to a local function breaks any reference to
    /// it inside an expression-tree lambda (CS8110) — delegate invocation is fine in an
    /// expression tree, a local function reference is not. Keep those as delegates.
    /// </summary>
    private bool IsReferencedInsideExpressionTree(ILocalSymbol localSymbol, SyntaxNode declaration)
    {
        var scope = (SyntaxNode)declaration.GetAncestor<VBSyntax.MethodBlockBaseSyntax>() ?? declaration.Parent;
        if (scope == null) return false;
        foreach (var id in scope.DescendantNodes().OfType<VBSyntax.IdentifierNameSyntax>()) {
            if (!id.Identifier.ValueText.Equals(localSymbol.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!SymbolEqualityComparer.Default.Equals(_semanticModel.GetSymbolInfo(id).Symbol, localSymbol)) continue;
            for (var ancestor = id.Parent; ancestor != null && ancestor != scope; ancestor = ancestor.Parent) {
                if (ancestor is VBSyntax.LambdaExpressionSyntax lambda &&
                    _semanticModel.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol { Name: nameof(System.Linq.Expressions.Expression), Arity: 1 } converted &&
                    converted.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions") {
                    return true;
                }
            }
        }
        return false;
    }

    private MethodDeclarationSyntax CreateMethodDeclaration(IAnonymousFunctionOperation operation,
        IFieldSymbol fieldSymbol,
        BlockSyntax block, ArrowExpressionClauseSyntax arrow)
    {
        MethodDeclarationSyntax methodDecl =
            ((MethodDeclarationSyntax)CommonConversions.CsSyntaxGenerator.MethodDeclaration(operation.Symbol))
            .WithIdentifier(SyntaxFactory.Identifier(fieldSymbol.Name))
            .WithBody(block).WithExpressionBody(arrow);

        if (operation.Symbol.IsAsync) methodDecl = methodDecl.AddModifiers(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));

        if (arrow != null) methodDecl = methodDecl.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        return methodDecl;
    }

    private LocalFunctionStatementSyntax CreateLocalFunction(IAnonymousFunctionOperation operation,
        IVariableDeclaratorOperation variableDeclaratorOperation,
        ParameterListSyntax param, BlockSyntax block,
        ArrowExpressionClauseSyntax arrow)
    {
        string symbolName = variableDeclaratorOperation.Symbol.Name;
        LocalFunctionStatementSyntax localFunc = SyntaxFactory.LocalFunctionStatement(
            SyntaxFactory.TokenList(),
            CommonConversions.GetTypeSyntax(operation.Symbol.ReturnType),
            SyntaxFactory.Identifier(symbolName), null, param,
            SyntaxFactory.List<TypeParameterConstraintClauseSyntax>(), block, arrow,
            SyntaxFactory.Token(SyntaxKind.SemicolonToken));


        if (operation.Symbol.IsAsync) localFunc = localFunc.AddModifiers(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));

        return localFunc;
    }
}