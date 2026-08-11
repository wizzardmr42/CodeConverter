using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace ICSharpCode.CodeConverter.CSharp;

/// <remarks>
///  Grammar info: https://web.archive.org/web/20170715190715/http://kursinfo.himolde.no/in-kurs/IBE150/VBspec.htm#_Toc248253288
/// </remarks>
internal class QueryConverter
{
    private readonly CommentConvertingVisitorWrapper _triviaConvertingVisitor;
    private readonly SemanticModel _semanticModel;
    private static readonly SyntaxAnnotation DefaultSelectAnnotation = new("DefaultSelect");

    public QueryConverter(CommonConversions commonConversions, SemanticModel semanticModel, CommentConvertingVisitorWrapper triviaConvertingExpressionVisitor)
    {
        CommonConversions = commonConversions;
        _semanticModel = semanticModel;
        _triviaConvertingVisitor = triviaConvertingExpressionVisitor;
    }

    private CommonConversions CommonConversions { get; }

    public async Task<CSharpSyntaxNode> ConvertClausesAsync(SyntaxList<VBSyntax.QueryClauseSyntax> clauses)
    {
        bool originalIsWithinQuery = _triviaConvertingVisitor.IsWithinQuery;
        _triviaConvertingVisitor.IsWithinQuery = true;
        try {
            var convertClausesInnerAsync = await ConvertClausesInnerAsync(clauses);
            return convertClausesInnerAsync;
        } finally {
            _triviaConvertingVisitor.IsWithinQuery = originalIsWithinQuery;
        }
    }

    public async Task<CSharpSyntaxNode> ConvertClausesInnerAsync(SyntaxList<VBSyntax.QueryClauseSyntax> clauses)
    {
        var vbBodyClauses = new Queue<VBSyntax.QueryClauseSyntax>(clauses);
        var vbStartClause = vbBodyClauses.Peek();
        CSSyntax.FromClauseSyntax fromClauseSyntaxFromAggregate = null;
        SyntaxToken reusableFromCsId;
        if (vbStartClause is VBSyntax.AggregateClauseSyntax agg) {
            vbBodyClauses.Dequeue();
            foreach (var queryOperators in agg.AdditionalQueryOperators) {
                vbBodyClauses.Enqueue(queryOperators);
            }

            fromClauseSyntaxFromAggregate = await ConvertAggregateToFromClauseSyntaxAsync(agg);
            reusableFromCsId = fromClauseSyntaxFromAggregate.Identifier.WithoutSourceMapping();
        } else if (vbStartClause is VBSyntax.FromClauseSyntax fcs) {
            reusableFromCsId = CommonConversions.ConvertIdentifier(fcs.Variables.First().Identifier.Identifier).WithoutSourceMapping();
            agg = null;
        } else {
            throw new NotImplementedException($"Start clause type '{vbStartClause.GetType()}' not yet implemented");
        }
            
        CSharpSyntaxNode rootExpression;
        if (vbBodyClauses.Any()) {
            var querySegments = await GetQuerySegmentsAsync(vbBodyClauses);
            rootExpression = await ConvertQuerySegmentsAsync(querySegments, reusableFromCsId, fromClauseSyntaxFromAggregate);
        } else {
            rootExpression = fromClauseSyntaxFromAggregate.Expression;
        }

        if (agg != null) {
            if (agg.AggregationVariables.Count == 1 &&
                agg.AggregationVariables.Single().Aggregation is VBSyntax.FunctionAggregationSyntax fas) {
                if (rootExpression is CSSyntax.QueryExpressionSyntax qes)
                    rootExpression = SyntaxFactory.ParenthesizedExpression(qes);
                var collectionRangeVariableSyntax = agg.Variables.Single();
                var toAggregate = await fas.Argument.AcceptAsync<CSharpSyntaxNode>(_triviaConvertingVisitor);
                var methodTocall =
                    SyntaxFactory.IdentifierName(CommonConversions.ConvertIdentifier(fas.FunctionName)); //TODO
                var rootWithMethodCall =
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        (CSSyntax.ExpressionSyntax)rootExpression, methodTocall);
                var parameterSyntax = SyntaxFactory.Parameter(
                    CommonConversions.ConvertIdentifier(collectionRangeVariableSyntax.Identifier.Identifier));
                var argumentSyntaxes = toAggregate != null
                    ? new[] {
                        SyntaxFactory.Argument(SyntaxFactory.SimpleLambdaExpression(
                            parameterSyntax, toAggregate))
                    }
                    : Array.Empty<CSSyntax.ArgumentSyntax>();
                var args = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(argumentSyntaxes));
                var variable = SyntaxFactory.InvocationExpression(rootWithMethodCall, args);
                return variable;
            } else {
                throw new NotImplementedException("Aggregate clause type not implemented");
            }
        }

        return rootExpression;
    }

    /// <summary>
    ///  TODO: Don't bother with reversing, rewrite ConvertQueryWithContinuation to recurse on them the right way around
    /// </summary>
    private async Task<List<(Queue<(SyntaxList<CSSyntax.QueryClauseSyntax>, VBSyntax.QueryClauseSyntax)>, VBSyntax.QueryClauseSyntax)>> GetQuerySegmentsAsync(Queue<VBSyntax.QueryClauseSyntax> vbBodyClauses)
    {
        var querySegments =
            new List<(Queue<(SyntaxList<CSSyntax.QueryClauseSyntax>, VBSyntax.QueryClauseSyntax)>,
                VBSyntax.QueryClauseSyntax)>();
        while (vbBodyClauses.Any()) {
            var querySectionsReversed =
                new Queue<(SyntaxList<CSSyntax.QueryClauseSyntax>, VBSyntax.QueryClauseSyntax)>();
            while (vbBodyClauses.Any() && !RequiresMethodInvocation(vbBodyClauses.Peek()) && !EndsInSelect(querySectionsReversed)) {
                var convertedClauses = new List<CSSyntax.QueryClauseSyntax>();
                while (IsPartOfSegment(vbBodyClauses)) {
                    convertedClauses.AddRange(await ConvertQueryBodyClauseAsync(vbBodyClauses.Dequeue()));
                }

                var convertQueryBodyClauses = (SyntaxFactory.List(convertedClauses),
                    vbBodyClauses.Any() && !RequiresMethodInvocation(vbBodyClauses.Peek()) ? vbBodyClauses.Dequeue() : null);
                querySectionsReversed.Enqueue(convertQueryBodyClauses);
            }
            querySegments.Add((querySectionsReversed, vbBodyClauses.Any() && !EndsInSelect(querySectionsReversed) ? vbBodyClauses.Dequeue() : null));
        }
        return querySegments;
    }

    private static bool EndsInSelect(Queue<(SyntaxList<CSSyntax.QueryClauseSyntax>, QueryClauseSyntax)> querySectionsReversed) =>
        querySectionsReversed.LastOrDefault().Item2 is VBSyntax.SelectClauseSyntax;

    private static bool IsPartOfSegment(Queue<QueryClauseSyntax> vbBodyClauses) =>
        vbBodyClauses.Any() && !RequiredContinuation(vbBodyClauses) && !RequiresMethodInvocation(vbBodyClauses.Peek());

    private static bool RequiredContinuation(Queue<QueryClauseSyntax> vbBodyClauses) =>
        RequiredContinuation(vbBodyClauses.Peek(), vbBodyClauses.Count - 1);

    private async Task<CSharpSyntaxNode> ConvertQuerySegmentsAsync(IEnumerable<(Queue<(SyntaxList<CSSyntax.QueryClauseSyntax>, VBSyntax.QueryClauseSyntax)>, VBSyntax.QueryClauseSyntax)> querySegments, SyntaxToken reusableFromCsId, CSSyntax.FromClauseSyntax fromClauseSyntax = null)
    {
        CSSyntax.ExpressionSyntax query = null;
        foreach (var (queryContinuation, queryEnd) in querySegments) {
            var subQuery = await ConvertQueryWithContinuationAsync(queryContinuation, reusableFromCsId);
            if (fromClauseSyntax == null) {
                fromClauseSyntax = subQuery.Clauses.OfType<CSSyntax.FromClauseSyntax>().First();
                subQuery = subQuery.WithClauses(subQuery.Clauses.Remove(fromClauseSyntax));
            }

            // e.g. `from x in xs select x` is not useful, so just use `xs` directly
            bool isUsefulQuery = subQuery is not null && (!subQuery.SelectOrGroup.HasAnnotation(DefaultSelectAnnotation) || subQuery.Clauses.Any());
            query = isUsefulQuery ? SyntaxFactory.QueryExpression(fromClauseSyntax, subQuery) : fromClauseSyntax.Expression;

            if (queryEnd is not null) {
                query = await ConvertQueryToLinqAsync(reusableFromCsId, queryEnd, query);
            }
            fromClauseSyntax = SyntaxFactory.FromClause(reusableFromCsId, query);
        }

        return query ?? throw new ArgumentOutOfRangeException(nameof(querySegments), querySegments, null);
    }

    private async Task<CSSyntax.InvocationExpressionSyntax> ConvertQueryToLinqAsync(SyntaxToken reusableCsFromId, VBSyntax.QueryClauseSyntax queryEnd,
        CSSyntax.ExpressionSyntax query)
    {
        var linqMethodName = GetLinqMethodName(queryEnd);
        var parenthesizedQuery = query is CSSyntax.QueryExpressionSyntax ? SyntaxFactory.ParenthesizedExpression(query) : query;
        var linqMethod = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, parenthesizedQuery,
            ValidSyntaxFactory.IdentifierName(linqMethodName));
        var linqArguments = await GetLinqArgumentsAsync(reusableCsFromId, queryEnd);
        var linqArgumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(linqArguments.Select(SyntaxFactory.Argument)));
        var invocationExpressionSyntax = SyntaxFactory.InvocationExpression(linqMethod, linqArgumentList);
        return invocationExpressionSyntax;
    }

    private async Task<CSSyntax.QueryBodySyntax> ConvertQueryWithContinuationAsync(Queue<(SyntaxList<CSSyntax.QueryClauseSyntax>, VBSyntax.QueryClauseSyntax)> querySectionsReversed, SyntaxToken reusableCsFromId)
    {
        if (!querySectionsReversed.Any()) return null;
        var (convertedClauses, clauseEnd) = querySectionsReversed.Dequeue();
        var nestedClause = await ConvertQueryWithContinuationAsync(querySectionsReversed, reusableCsFromId);
        var convertSubQueryAsync = await ConvertSubQueryAsync(reusableCsFromId, clauseEnd, nestedClause, convertedClauses);
        return convertSubQueryAsync;
    }

    private async Task<CSSyntax.QueryBodySyntax> ConvertSubQueryAsync(SyntaxToken reusableCsFromId, VBSyntax.QueryClauseSyntax clauseEnd,
        CSSyntax.QueryBodySyntax nestedClause, SyntaxList<CSSyntax.QueryClauseSyntax> convertedClauses)
    {
        CSSyntax.SelectOrGroupClauseSyntax selectOrGroup;
        CSSyntax.QueryContinuationSyntax queryContinuation = null;
        switch (clauseEnd) {
            case null:
                // If a Group Join `Into <name>` is in scope, VB's implicit projection
                // is `{ <from-var>, <into-var> }`. C#'s default `select <from-var>`
                // loses <into-var>, and downstream references like `c.sc` or
                // `c.assignedDetails` fail (issue #29-adjacent). Emit an explicit
                // anonymous type projection when we see one.
                var groupJoinIntos = convertedClauses.OfType<CSSyntax.JoinClauseSyntax>()
                    .Where(j => j.Into != null)
                    .Select(j => j.Into.Identifier)
                    .ToList();
                if (groupJoinIntos.Any()) {
                    var members = new List<CSSyntax.AnonymousObjectMemberDeclaratorSyntax> {
                        SyntaxFactory.AnonymousObjectMemberDeclarator(
                            ValidSyntaxFactory.IdentifierName(reusableCsFromId))
                    };
                    foreach (var intoId in groupJoinIntos) {
                        members.Add(SyntaxFactory.AnonymousObjectMemberDeclarator(
                            ValidSyntaxFactory.IdentifierName(intoId)));
                    }
                    var anon = SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(members));
                    selectOrGroup = SyntaxFactory.SelectClause(anon);
                } else {
                    selectOrGroup = CreateDefaultSelectClause(reusableCsFromId).WithAdditionalAnnotations(DefaultSelectAnnotation);
                }
                break;
            case VBSyntax.GroupByClauseSyntax gcs:
                var groupKeyIds = GetGroupKeyIdentifiers(gcs).ToList();

                var continuationClauses = SyntaxFactory.List<CSSyntax.QueryClauseSyntax>();
                // Bind the group key and each aggregation variable to a `let` clause
                // so the nested Select's bare references (`Select BandID, foo = ...`)
                // resolve — VB's `Group By .. Into ..` promotes both to the projection
                // namespace, C# needs explicit lets.
                var groupIdentifierForLet = GetGroupIdentifier(gcs);
                if (nestedClause != null) {
                    if (groupKeyIds.Count == 1) {
                        var letGroupKey = SyntaxFactory.LetClause(groupKeyIds.First(),
                            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                ValidSyntaxFactory.IdentifierName(groupIdentifierForLet),
                                ValidSyntaxFactory.IdentifierName("Key")));
                        continuationClauses = continuationClauses.Add(letGroupKey);
                    }
                    // Also add lets for aggregation variables so `Select <aggName>`
                    // works. Sources of the aggregation NAME:
                    //   - Explicit `Into <name> = <expr>`      -> NameEquals
                    //   - Bare `Into <FuncName>` (function agg) -> FunctionName
                    //   - Bare `Into Group`                     -> no let, handled via group identifier
                    foreach (var agg in gcs.AggregationVariables) {
                        SyntaxToken? aggNameTokenOrNull = agg.NameEquals?.Identifier.Identifier
                            ?? (agg.Aggregation is VBSyntax.FunctionAggregationSyntax fnAgg ? fnAgg.FunctionName : (SyntaxToken?)null);
                        if (aggNameTokenOrNull is not { } aggName) continue;
                        // Skip if the aggregation is bare Group and its explicit name
                        // matches the group identifier — that becomes `let g = g` which
                        // C# rejects (self-referential range variable).
                        if (agg.Aggregation is VBSyntax.GroupAggregationSyntax
                            && string.Equals(aggName.ValueText, groupIdentifierForLet.ValueText, StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }
                        CSSyntax.ExpressionSyntax aggExpr = agg.Aggregation switch {
                            VBSyntax.GroupAggregationSyntax => ValidSyntaxFactory.IdentifierName(groupIdentifierForLet),
                            VBSyntax.FunctionAggregationSyntax fa => SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    ValidSyntaxFactory.IdentifierName(groupIdentifierForLet),
                                    ValidSyntaxFactory.IdentifierName(fa.FunctionName.Text))),
                            _ => ValidSyntaxFactory.IdentifierName(groupIdentifierForLet)
                        };
                        continuationClauses = continuationClauses.Add(SyntaxFactory.LetClause(aggName.Text, aggExpr));
                    }
                } else if (groupKeyIds.Count == 1) {
                    var letGroupKey = SyntaxFactory.LetClause(groupKeyIds.First(), SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ValidSyntaxFactory.IdentifierName(groupIdentifierForLet), ValidSyntaxFactory.IdentifierName("Key")));
                    continuationClauses = continuationClauses.Add(letGroupKey);
                }
                if (!gcs.Items.Any()) {
                    var identifierNameSyntax =
                        ValidSyntaxFactory.IdentifierName(reusableCsFromId);
                    selectOrGroup = SyntaxFactory.GroupClause(identifierNameSyntax, await GetGroupExpressionAsync(gcs));
                } else {
                    var item = await gcs.Items.Single().Expression.AcceptAsync<CSSyntax.IdentifierNameSyntax>(_triviaConvertingVisitor);
                    var keyExpression = await gcs.Keys.Single().Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
                    selectOrGroup = SyntaxFactory.GroupClause(item, keyExpression);
                }
                if (nestedClause != null) {
                    continuationClauses = continuationClauses.AddRange(nestedClause.Clauses);
                    queryContinuation = CreateGroupByContinuation(gcs, continuationClauses, nestedClause.SelectOrGroup);
                } else if (RequiresProjectionContinuation(gcs, groupKeyIds)) {
                    // VB `Group By k1, k2 Into Group` produces an anonymous type
                    // `{ k1, k2, Group }` where downstream `gg.k1` and `gg.Group`
                    // both work. Emitting a bare `group x by ...` in C# gives an
                    // `IGrouping<K,T>` where `.k1` / `.Group` aren't valid. Add a
                    // `into @group select new { @group.Key.k1, @group.Key.k2,
                    // Group = @group }` continuation to restore the shape.
                    var projectionSelect = await CreateGroupByProjectionAsync(gcs, GetGroupIdentifier(gcs));
                    queryContinuation = CreateGroupByContinuation(gcs, continuationClauses, projectionSelect);
                }
                break;
            case VBSyntax.SelectClauseSyntax scs:
                selectOrGroup = await ConvertSelectClauseSyntaxAsync(scs);
                break;
            default:
                throw new NotImplementedException($"Clause kind '{clauseEnd.Kind()}' is not yet implemented");
        }

        return SyntaxFactory.QueryBody(convertedClauses, selectOrGroup, queryContinuation);
    }

    private CSSyntax.QueryContinuationSyntax CreateGroupByContinuation(VBSyntax.GroupByClauseSyntax gcs, SyntaxList<CSSyntax.QueryClauseSyntax> convertedClauses, CSSyntax.SelectOrGroupClauseSyntax selectOrGroupClauseSyntax)
    {
        var queryBody = convertedClauses.Any() ? SyntaxFactory.QueryBody(convertedClauses, selectOrGroupClauseSyntax, null) : SyntaxFactory.QueryBody(selectOrGroupClauseSyntax);
        SyntaxToken groupName = GetGroupIdentifier(gcs);
        if (queryBody.SelectOrGroup.HasAnnotation(DefaultSelectAnnotation)) {
            queryBody = queryBody.WithSelectOrGroup(CreateDefaultSelectClause(groupName));
        }
        return SyntaxFactory.QueryContinuation(groupName, queryBody);
    }

    private async Task<IEnumerable<CSSyntax.ExpressionSyntax>> GetLinqArgumentsAsync(SyntaxToken reusableCsFromId,
        VBSyntax.QueryClauseSyntax linqQuery)
    {
        switch (linqQuery) {
            case VBSyntax.DistinctClauseSyntax _:
                return Enumerable.Empty<CSSyntax.ExpressionSyntax>();
            case VBSyntax.PartitionClauseSyntax pcs:
                return new[] {await pcs.Count.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor)};
            case VBSyntax.PartitionWhileClauseSyntax pwcs: {
                var lambdaParam = SyntaxFactory.Parameter(reusableCsFromId);
                var lambdaBody = await pwcs.Condition.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
                return new CSSyntax.ExpressionSyntax[] {SyntaxFactory.SimpleLambdaExpression(lambdaParam, lambdaBody)};
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(linqQuery), linqQuery.Kind(), null);
        }
    }

    private static string GetLinqMethodName(VBSyntax.QueryClauseSyntax queryEnd)
    {
        switch (queryEnd) {
            case VBSyntax.DistinctClauseSyntax _:
                return nameof(Enumerable.Distinct);
            case VBSyntax.PartitionClauseSyntax pcs:
                return pcs.SkipOrTakeKeyword.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.SkipKeyword) ? nameof(Enumerable.Skip) : nameof(Enumerable.Take);
            case VBSyntax.PartitionWhileClauseSyntax pwcs:
                return pwcs.SkipOrTakeKeyword.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.SkipKeyword) ? nameof(Enumerable.SkipWhile) : nameof(Enumerable.TakeWhile);
            default:
                throw new ArgumentOutOfRangeException(nameof(queryEnd), queryEnd.Kind(), null);
        }
    }

    private static bool RequiresMethodInvocation(VBSyntax.QueryClauseSyntax queryClauseSyntax)
    {
        return queryClauseSyntax is VBSyntax.PartitionClauseSyntax
               || queryClauseSyntax is VBSyntax.PartitionWhileClauseSyntax
               || queryClauseSyntax is VBSyntax.DistinctClauseSyntax;
    }

    /// <summary>
    /// In VB, multiple selects work like Let clauses, but the last one needs to become the actual select (its name is discarded)
    /// </summary>
    private static bool RequiredContinuation(VBSyntax.QueryClauseSyntax queryClauseSyntax, int clausesAfter) => queryClauseSyntax is VBSyntax.GroupByClauseSyntax
                                                                                                                || queryClauseSyntax is VBSyntax.SelectClauseSyntax sc && (sc.Variables.Any(v => v.NameEquals is null) || clausesAfter == 0);

    private async Task<IEnumerable<CSSyntax.FromClauseSyntax>> ConvertFromClauseSyntaxAsync(VBSyntax.FromClauseSyntax vbFromClause) => await vbFromClause.Variables.SelectAsync(ConvertFromClauseVariableAsync);

    private async Task<CSSyntax.FromClauseSyntax> ConvertFromClauseVariableAsync(CollectionRangeVariableSyntax collectionRangeVariableSyntax)
    {
        var expression = await collectionRangeVariableSyntax.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
        var parentOperation = _semanticModel.GetOperation(collectionRangeVariableSyntax.Expression)?.Parent;
        if (parentOperation != null && parentOperation.IsImplicit && parentOperation is IInvocationOperation io &&
            io.TargetMethod.MethodKind == MethodKind.ReducedExtension && io.TargetMethod.Name == nameof(Enumerable.AsEnumerable)) {
            expression = SyntaxFactory.InvocationExpression(ValidSyntaxFactory.MemberAccess(expression, io.TargetMethod.Name), SyntaxFactory.ArgumentList());
        }
        var fromClauseSyntax = SyntaxFactory.FromClause(
            CommonConversions.ConvertIdentifier(collectionRangeVariableSyntax.Identifier.Identifier),
            expression);
        return fromClauseSyntax;
    }

    private async Task<CSSyntax.FromClauseSyntax> ConvertAggregateToFromClauseSyntaxAsync(VBSyntax.AggregateClauseSyntax vbAggClause)
    {
        var collectionRangeVariableSyntax = vbAggClause.Variables.Single();
        var fromClauseSyntax = SyntaxFactory.FromClause(
            CommonConversions.ConvertIdentifier(collectionRangeVariableSyntax.Identifier.Identifier),
            await collectionRangeVariableSyntax.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor));
        return fromClauseSyntax;
    }

    private async Task<CSSyntax.SelectClauseSyntax> ConvertSelectClauseSyntaxAsync(VBSyntax.SelectClauseSyntax vbSelectClause)
    {
        var selectedVariables = await vbSelectClause.Variables.SelectAsync(async v => {
            var nameEquals = await v.NameEquals.AcceptAsync<CSSyntax.NameEqualsSyntax>(_triviaConvertingVisitor);
            var expression = await v.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
            return SyntaxFactory.AnonymousObjectMemberDeclarator(nameEquals, expression);
        });

        if (selectedVariables.Length == 1)
            return SyntaxFactory.SelectClause(selectedVariables.Single().Expression);
        return SyntaxFactory.SelectClause(SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(selectedVariables)));
    }

    /// <summary>
    /// TODO: In the case of multiple Froms and no Select, VB returns an anonymous type containing all the variables created by the from clause
    /// </summary>
    private static CSSyntax.SelectClauseSyntax CreateDefaultSelectClause(SyntaxToken reusableCsFromId)
    {
        return SyntaxFactory.SelectClause(ValidSyntaxFactory.IdentifierName(reusableCsFromId));
    }

    private async Task<IEnumerable<CSSyntax.QueryClauseSyntax>> ConvertQueryBodyClauseAsync(VBSyntax.QueryClauseSyntax node)
    {
        return node switch {
            VBSyntax.FromClauseSyntax x => await ConvertFromClauseSyntaxAsync(x),
            VBSyntax.JoinClauseSyntax x => await ConvertJoinClauseAsync(x).YieldAsync(),
            VBSyntax.SelectClauseSyntax x => await ConvertSelectClauseAsync(x).YieldAsync(),
            VBSyntax.LetClauseSyntax x => await ConvertLetClauseAsync(x).YieldAsync(),
            VBSyntax.OrderByClauseSyntax x => await ConvertOrderByClauseAsync(x).YieldAsync(),
            VBSyntax.WhereClauseSyntax x => await ConvertWhereClauseAsync(x).YieldAsync(),
            _ => throw new NotImplementedException($"Conversion for query clause with kind '{node.Kind()}' not implemented")
        };
    }

    // We need a projection continuation when downstream code will use bare key
    // names or `.Group` on the query result. The composite-key `Group By k1, k2
    // Into Group` case is the always-broken shape — codeconv currently emits a
    // bare group clause and the result becomes an unhelpful `IGrouping<K,T>`.
    // For single-key + Let (the existing code path), the let clause already
    // gives downstream access, so we don't need a projection there.
    private static bool RequiresProjectionContinuation(VBSyntax.GroupByClauseSyntax gcs, List<string> groupKeyIds)
    {
        // Any Into <aggregation> requires a projection continuation, because VB
        // promotes both the key(s) and the aggregation(s) to the anonymous shape
        // downstream code will use. An IGrouping<K,T> alone doesn't expose the
        // aggregation as a member — it needs the explicit select projection.
        // The single-key path adds a `let <keyName> = @group.Key` in the
        // continuation clauses, but without a continuation those lets are dropped.
        return gcs.AggregationVariables.Any();
    }

    private async Task<CSSyntax.SelectClauseSyntax> CreateGroupByProjectionAsync(VBSyntax.GroupByClauseSyntax gcs, SyntaxToken groupName)
    {
        var groupIdName = ValidSyntaxFactory.IdentifierName(groupName);
        var keyAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            groupIdName,
            ValidSyntaxFactory.IdentifierName("Key"));

        var members = new List<CSSyntax.AnonymousObjectMemberDeclaratorSyntax>();
        bool singleKey = gcs.Keys.Count == 1;

        // For each key:
        //   Single key → `<name> = @group.Key` (Key IS the value directly, no anon type).
        //   Multi key  → `@group.Key.<name>` (Key IS an anon type; member access exposes name).
        int keyIndex = 0;
        foreach (var key in gcs.Keys) {
            var nameToken = key.NameEquals?.Identifier.Identifier
                            ?? key.Expression.ExtractAnonymousTypeMemberName()
                            ?? SyntaxFactory.Identifier("key" + keyIndex);
            if (singleKey) {
                members.Add(SyntaxFactory.AnonymousObjectMemberDeclarator(
                    SyntaxFactory.NameEquals(ValidSyntaxFactory.IdentifierName(nameToken.Text)),
                    keyAccess));
            } else {
                var memberAccess = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    keyAccess,
                    ValidSyntaxFactory.IdentifierName(nameToken.Text));
                members.Add(SyntaxFactory.AnonymousObjectMemberDeclarator(memberAccess));
            }
            keyIndex++;
        }

        // For each aggregation variable, emit `<name> = <expr>`.
        // - `Into Group` (GroupAggregationSyntax): expr is the group identifier itself
        // - `Into Foo = Count()` etc.: apply the function to the group
        int aggIndex = 0;
        foreach (var agg in gcs.AggregationVariables) {
            // Aggregation name sources:
            //   Into <name> = ...           -> NameEquals
            //   Into Group                  -> literal "Group"
            //   Into <FuncName> (bare)      -> FunctionName from FunctionAggregationSyntax
            var aggName = agg.NameEquals?.Identifier.Identifier.Text
                          ?? (agg.Aggregation is VBSyntax.FunctionAggregationSyntax fnAgg ? fnAgg.FunctionName.Text
                              : agg.Aggregation is VBSyntax.GroupAggregationSyntax ? "Group"
                              : "agg" + aggIndex);
            CSSyntax.ExpressionSyntax aggExpr;
            switch (agg.Aggregation) {
                case VBSyntax.GroupAggregationSyntax:
                    aggExpr = groupIdName;
                    break;
                case VBSyntax.FunctionAggregationSyntax fa:
                    var invocationTarget = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        groupIdName,
                        ValidSyntaxFactory.IdentifierName(fa.FunctionName.Text));
                    aggExpr = SyntaxFactory.InvocationExpression(invocationTarget);
                    break;
                default:
                    aggExpr = groupIdName;
                    break;
            }
            members.Add(SyntaxFactory.AnonymousObjectMemberDeclarator(
                SyntaxFactory.NameEquals(ValidSyntaxFactory.IdentifierName(aggName)),
                aggExpr));
            aggIndex++;
        }

        var anon = SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(members));
        return SyntaxFactory.SelectClause(anon);
    }

    private async Task<CSSyntax.ExpressionSyntax> GetGroupExpressionAsync(VBSyntax.GroupByClauseSyntax gs)
    {
        var groupExpressions = (await gs.Keys.SelectAsync(async k => (vb: k.Expression, cs: await k.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor)))).ToList();
        return (groupExpressions.Count == 1) ? groupExpressions.Single().cs : CreateAnonymousType(groupExpressions);
    }

    private static CSSyntax.ExpressionSyntax CreateAnonymousType(List<(ExpressionSyntax vb, CSSyntax.ExpressionSyntax cs)> groupExpressions)
    {
        return SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(groupExpressions.Select(CreateAnonymousMember)));
    }

    private static CSSyntax.AnonymousObjectMemberDeclaratorSyntax CreateAnonymousMember((ExpressionSyntax vb, CSSyntax.ExpressionSyntax cs) expr, int i)
    {
        var name = SyntaxFactory.Identifier(expr.vb.ExtractAnonymousTypeMemberName()?.Text ?? ("key" + i));
        return SyntaxFactory.AnonymousObjectMemberDeclarator(SyntaxFactory.NameEquals(ValidSyntaxFactory.IdentifierName(name)), expr.cs);
    }

    private SyntaxToken GetGroupIdentifier(VBSyntax.GroupByClauseSyntax gs)
    {
        if (!gs.Items.Any()) return CommonConversions.CsEscapedIdentifier("Group");
        var name = gs.AggregationVariables.Select(v => v.Aggregation switch {
            VBSyntax.FunctionAggregationSyntax f => f.FunctionName,
            VBSyntax.GroupAggregationSyntax => v.NameEquals?.Identifier.Identifier,
            _ => default
        }).Concat(gs.Keys.Select(k => k.NameEquals?.Identifier.Identifier)).FirstOrDefault(x => x != null);
        return name is {} n ? CommonConversions.ConvertIdentifier(n) : SyntaxFactory.Identifier("@group");
    }

    private static IEnumerable<string> GetGroupKeyIdentifiers(VBSyntax.GroupByClauseSyntax gs)
    {
        // A key's identifier comes from either:
        //   `k = <expr>`  -> NameEquals identifier (explicit)
        //   `<obj>.<Prop>` (bare) -> the trailing member access identifier (implicit,
        //                             matches VB's anonymous type naming rule)
        // The implicit case matters for the single-key `Group By x.Foo` shape —
        // downstream code references `Foo` bare, so we need a `let Foo = @group.Key`.
        return gs.Keys
            .Select(k => k.NameEquals?.Identifier.Identifier.Text
                         ?? k.Expression.ExtractAnonymousTypeMemberName()?.Text)
            .Where(x => x != null);
    }

    private async Task<CSSyntax.QueryClauseSyntax> ConvertWhereClauseAsync(VBSyntax.WhereClauseSyntax ws)
    {
        return SyntaxFactory.WhereClause(await ws.Condition.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor));
    }

    private async Task<CSSyntax.QueryClauseSyntax> ConvertSelectClauseAsync(VBSyntax.SelectClauseSyntax sc)
    {
        var singleVariable = sc.Variables.Single();
        var identifier = CommonConversions.ConvertIdentifier(singleVariable.NameEquals.Identifier.Identifier);
        return SyntaxFactory.LetClause(identifier, await singleVariable.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor));
    }

    private async Task<CSSyntax.QueryClauseSyntax> ConvertLetClauseAsync(VBSyntax.LetClauseSyntax ls)
    {
        var singleVariable = ls.Variables.Single();
        var identifier = CommonConversions.ConvertIdentifier(singleVariable.NameEquals.Identifier.Identifier);
        return SyntaxFactory.LetClause(identifier, await singleVariable.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor));
    }

    private async Task<CSSyntax.QueryClauseSyntax> ConvertOrderByClauseAsync(VBSyntax.OrderByClauseSyntax os)
    {
        var orderingSyntaxs = await os.Orderings.SelectAsync(async o => await o.AcceptAsync<CSSyntax.OrderingSyntax>(_triviaConvertingVisitor));
        return SyntaxFactory.OrderByClause(SyntaxFactory.SeparatedList(orderingSyntaxs));
    }

    private async Task<CSSyntax.QueryClauseSyntax> ConvertJoinClauseAsync(VBSyntax.JoinClauseSyntax js)
    {
        var variable = js.JoinedVariables.Single();
        var convertIdentifier = CommonConversions.ConvertIdentifier(variable.Identifier.Identifier);

        var joinLhsExpressions = await js.JoinConditions.SelectAsync(async c =>
            await c.Left.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor));

        var joinRhsExpressions = await js.JoinConditions.SelectAsync(async c =>
            await c.Right.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor));

        var (lhsAnonymousExpression, rhsAnonymousExpression) = CreateJoinAnonymousObjectKeys(joinLhsExpressions
            .Zip(joinRhsExpressions, (lhs, rhs) => (Lhs: lhs, Rhs: rhs))
            .ToList(), convertIdentifier);

        var expressionSyntax = await variable.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);

        CSSyntax.JoinIntoClauseSyntax joinIntoClauseSyntax = null;
        if (js is VBSyntax.GroupJoinClauseSyntax gjs) {
            joinIntoClauseSyntax = gjs.AggregationVariables
                .Where(a => a.Aggregation is VBSyntax.GroupAggregationSyntax)
                .Select(a => SyntaxFactory.JoinIntoClause(CommonConversions.ConvertIdentifier(a.NameEquals.Identifier.Identifier)))
                .SingleOrDefault();
        }

        return SyntaxFactory.JoinClause(null, convertIdentifier, expressionSyntax, lhsAnonymousExpression,
            rhsAnonymousExpression, joinIntoClauseSyntax);
    }

    private static (CSSyntax.ExpressionSyntax Lhs, CSSyntax.ExpressionSyntax Rhs) CreateJoinAnonymousObjectKeys(IEnumerable<(CSSyntax.ExpressionSyntax Lhs, CSSyntax.ExpressionSyntax Rhs)> expressions,
        SyntaxToken convertIdentifier)
    {
        // C# enforces specific ordering of range variables around the equals token inside a join clause (CS1937)
        var swappedExpressions = expressions
            .Select(expression => {
                return expression.Lhs switch
                {
                    CSSyntax.MemberAccessExpressionSyntax mac => mac.Expression is not CSSyntax.IdentifierNameSyntax idNameSyntax ||
                                                                 idNameSyntax.Identifier.ValueText != convertIdentifier.ValueText
                        ? expression
                        : SwapExpressions(expression),


                    CSSyntax.IdentifierNameSyntax idName => idName.Identifier.ValueText != convertIdentifier.ValueText
                        ? expression
                        : SwapExpressions(expression),

                    _ => throw new NotImplementedException($"Conversion for join query clause with condition of kind '{expression.Lhs.Kind()}' not implemented")
                };
            })
            .ToList();

        if (swappedExpressions.Count == 1) return swappedExpressions.Single();

        static CSSyntax.AnonymousObjectCreationExpressionSyntax AnonCreateFunc(
            IEnumerable<(CSSyntax.ExpressionSyntax Lhs, CSSyntax.ExpressionSyntax Rhs)> exp, bool isLhs)
        {
            var keySeparatedList = SyntaxFactory.SeparatedList(exp.Select((se, i) =>
            {
                var keyNameEquals = SyntaxFactory.NameEquals($"key{i}");
                var anonObjectDeclarator = SyntaxFactory.AnonymousObjectMemberDeclarator(keyNameEquals, isLhs ? se.Lhs : se.Rhs);

                return anonObjectDeclarator;
            }));

            var anonObjectExpression = SyntaxFactory.AnonymousObjectCreationExpression(keySeparatedList);

            return anonObjectExpression;
        }

        var lhsAnonymousObjectExpressions = AnonCreateFunc(swappedExpressions, true);
        var rhsAnonymousObjectExpressions = AnonCreateFunc(swappedExpressions, false);

        return (lhsAnonymousObjectExpressions, rhsAnonymousObjectExpressions);
    }

    /// <summary>
    /// This method swaps the lhs and rhs of <paramref name="expression"/>
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    private static (CSSyntax.ExpressionSyntax Lhs, CSSyntax.ExpressionSyntax Rhs) SwapExpressions(
        (CSSyntax.ExpressionSyntax Lhs, CSSyntax.ExpressionSyntax Rhs) expression)
    {
        var (lhs, rhs) = expression;
        expression.Lhs = rhs;
        expression.Rhs = lhs;

        return expression;
    }
}