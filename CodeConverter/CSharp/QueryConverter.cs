using Microsoft.CodeAnalysis.CSharp;
using ICSharpCode.CodeConverter.Util.FromRoslyn;
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
    private async Task<List<(Queue<QuerySection>, VBSyntax.QueryClauseSyntax)>> GetQuerySegmentsAsync(Queue<VBSyntax.QueryClauseSyntax> vbBodyClauses)
    {
        var querySegments =
            new List<(Queue<QuerySection>,
                VBSyntax.QueryClauseSyntax)>();
        while (vbBodyClauses.Any()) {
            var querySectionsReversed =
                new Queue<QuerySection>();
            while (vbBodyClauses.Any() && !RequiresMethodInvocation(vbBodyClauses.Peek()) && !EndsInSelect(querySectionsReversed)) {
                var convertedClauses = new List<CSSyntax.QueryClauseSyntax>();
                var vbClauses = new List<VBSyntax.QueryClauseSyntax>();
                while (IsPartOfSegment(vbBodyClauses)) {
                    var vbClause = vbBodyClauses.Dequeue();
                    vbClauses.Add(vbClause);
                    convertedClauses.AddRange(await ConvertQueryBodyClauseAsync(vbClause));
                }

                var convertQueryBodyClauses = new QuerySection(SyntaxFactory.List(convertedClauses),
                    vbBodyClauses.Any() && !RequiresMethodInvocation(vbBodyClauses.Peek()) ? vbBodyClauses.Dequeue() : null,
                    vbClauses);
                querySectionsReversed.Enqueue(convertQueryBodyClauses);
            }
            querySegments.Add((querySectionsReversed, vbBodyClauses.Any() && !EndsInSelect(querySectionsReversed) ? vbBodyClauses.Dequeue() : null));
        }
        return querySegments;
    }

    internal sealed record QuerySection(SyntaxList<CSSyntax.QueryClauseSyntax> ConvertedClauses, VBSyntax.QueryClauseSyntax ClauseEnd, IReadOnlyList<VBSyntax.QueryClauseSyntax> VbClauses);

    private static bool EndsInSelect(Queue<QuerySection> querySectionsReversed) =>
        querySectionsReversed.LastOrDefault()?.ClauseEnd is VBSyntax.SelectClauseSyntax;

    /// <summary>
    /// Tracks VB's transparent-identifier scope through a run of query
    /// clauses: From/Join/Let EXTEND the set of live range variable names, a
    /// Select (or Group By) REPLACES it. `changed` reports whether anything
    /// modified the incoming set — an unchanged set means the element shape
    /// is whatever flowed in, so an implicit select can stay a pass-through.
    /// </summary>
    private (List<string> Names, bool Changed) AdvanceLiveNames(IReadOnlyList<string> current, IEnumerable<VBSyntax.QueryClauseSyntax> vbClauses)
    {
        var live = current.ToList();
        bool changed = false;
        void Add(SyntaxToken vbIdentifier)
        {
            var name = CommonConversions.ConvertIdentifier(vbIdentifier).ValueText;
            if (!live.Contains(name, StringComparer.Ordinal)) {
                live.Add(name);
                changed = true;
            }
        }
        foreach (var clause in vbClauses ?? Enumerable.Empty<VBSyntax.QueryClauseSyntax>()) {
            switch (clause) {
                case VBSyntax.FromClauseSyntax f:
                    foreach (var v in f.Variables) Add(v.Identifier.Identifier);
                    break;
                case VBSyntax.SimpleJoinClauseSyntax j:
                    foreach (var v in j.JoinedVariables) Add(v.Identifier.Identifier);
                    break;
                case VBSyntax.GroupJoinClauseSyntax gj:
                    // The joined variable goes OUT of scope; the Into vars come in.
                    foreach (var agg in gj.AggregationVariables) {
                        if (agg.NameEquals?.Identifier.Identifier is { } n) Add(n);
                    }
                    break;
                case VBSyntax.LetClauseSyntax l:
                    foreach (var v in l.Variables) {
                        if (v.NameEquals?.Identifier.Identifier is { } n) Add(n);
                    }
                    break;
                case VBSyntax.SelectClauseSyntax s:
                    var selected = s.Variables
                        .Select(v => (v.NameEquals?.Identifier.Identifier ?? v.Expression.ExtractAnonymousTypeMemberName()) is { } t
                            ? CommonConversions.ConvertIdentifier(t).ValueText : null)
                        .Where(n => n != null)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (selected.Count == s.Variables.Count) {
                        live = selected;
                        changed = true;
                    }
                    break;
                case VBSyntax.GroupByClauseSyntax g:
                    var groupNames = GetGroupKeyIdentifiers(g)
                        .Concat(g.AggregationVariables.Select(a => a.NameEquals?.Identifier.Identifier.Text
                            ?? (a.Aggregation is VBSyntax.FunctionAggregationSyntax fn ? fn.FunctionName.Text
                                : a.Aggregation is VBSyntax.GroupAggregationSyntax ? "Group" : null)))
                        .Where(n => n != null)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (groupNames.Any()) {
                        live = groupNames;
                        changed = true;
                    }
                    break;
            }
        }
        return (live, changed);
    }

    private static bool IsPartOfSegment(Queue<QueryClauseSyntax> vbBodyClauses) =>
        vbBodyClauses.Any() && !RequiredContinuation(vbBodyClauses) && !RequiresMethodInvocation(vbBodyClauses.Peek());

    private static bool RequiredContinuation(Queue<QueryClauseSyntax> vbBodyClauses)
    {
        if (RequiredContinuation(vbBodyClauses.Peek(), vbBodyClauses.Count - 1)) return true;
        // A single-var Select REPLACES the element (VB puts prior range vars
        // out of scope). The let-emission fallback keeps the old element alive
        // — harmless while everything stays in one query, but wrong once a
        // method-invocation boundary (Distinct/Skip/Take) materialises the
        // element: Distinct dedups the stale {old, new} shape instead of the
        // selected value, and the new name is dropped at the boundary
        // (CS0103). Force a real `select` in that case; the rename rebinding
        // in ConvertQuerySegmentsAsync gives downstream clauses the right
        // range variable.
        if (vbBodyClauses.Peek() is VBSyntax.SelectClauseSyntax { Variables.Count: 1 }
            && vbBodyClauses.Skip(1).Any(RequiresMethodInvocation)) {
            return true;
        }
        // `Select ch = TryCast(ch, ...)` — a rename that REUSES the range
        // variable's own name. The let-emission `let ch = ...` collides with
        // the in-scope `ch` (CS1930); a real select ends the segment and the
        // next segment's `from ch in (...)` rebinds the name cleanly.
        return vbBodyClauses.Peek() is VBSyntax.SelectClauseSyntax { Variables.Count: 1 } selfRename
               && selfRename.Variables[0].NameEquals?.Identifier.Identifier.ValueText is { } assignedName
               && selfRename.Variables[0].Expression.DescendantNodesAndSelf().OfType<VBSyntax.IdentifierNameSyntax>()
                   .Any(id => id.Identifier.ValueText.Equals(assignedName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CSharpSyntaxNode> ConvertQuerySegmentsAsync(IEnumerable<(Queue<QuerySection>, VBSyntax.QueryClauseSyntax)> querySegments, SyntaxToken reusableFromCsId, CSSyntax.FromClauseSyntax fromClauseSyntax = null)
    {
        CSSyntax.ExpressionSyntax query = null;
        IReadOnlyCollection<string> anonMembersInScope = null;
        foreach (var (queryContinuation, queryEnd) in querySegments) {
            // Capture the segment's last VB clause BEFORE ConvertQueryWith-
            // ContinuationAsync drains the queue — used to detect a
            // single-item Select renaming the range variable.
            var lastVbClauseInSegment = queryContinuation.LastOrDefault()?.ClauseEnd;
            var segmentSeedNames = (IReadOnlyList<string>)(anonMembersInScope?.ToList() ?? new List<string> { reusableFromCsId.ValueText });
            var subQuery = await ConvertQueryWithContinuationAsync(queryContinuation, reusableFromCsId, segmentSeedNames);
            if (fromClauseSyntax == null) {
                fromClauseSyntax = subQuery.Clauses.OfType<CSSyntax.FromClauseSyntax>().First();
                subQuery = subQuery.WithClauses(subQuery.Clauses.Remove(fromClauseSyntax));
            }

            // A previous segment produced an anonymous element (`select new
            // { ooi, o, HasPicked }`). VB's transparent identifier lets
            // downstream clauses reference those members bare; in C# they're
            // members of this segment's range variable — qualify them
            // (`HasPicked` -> `ooi.HasPicked`, `select ooi` -> `select
            // ooi.ooi`) or they fail with CS0103.
            if (anonMembersInScope != null && subQuery != null) {
                subQuery = (CSSyntax.QueryBodySyntax)new QualifyAnonMembersRewriter(reusableFromCsId.ValueText, anonMembersInScope).Visit(subQuery);
            }

            // e.g. `from x in xs select x` is not useful, so just use `xs` directly.
            // But `from short i in xs select i` IS useful: an explicitly typed range
            // variable carries a per-element conversion (C# compiles it to
            // `xs.Cast<short>()`, VB to a CType per element). Collapsing to `xs`
            // silently dropped that, so `Dim a = (From i As Short In s.Split(",")).ToArray`
            // came out as `short[] a = s.Split(',').ToArray()` — CS0029, string[] to short[].
            bool typedRangeVariable = fromClauseSyntax.Type is not null;
            bool isUsefulQuery = subQuery is not null &&
                (!subQuery.SelectOrGroup.HasAnnotation(DefaultSelectAnnotation) || subQuery.Clauses.Any() || typedRangeVariable);
            query = isUsefulQuery ? SyntaxFactory.QueryExpression(fromClauseSyntax, subQuery) : fromClauseSyntax.Expression;

            // Track the shape flowing into the next segment: a final anon
            // select starts (or replaces) the member set; an implicit/default
            // select passes the current shape through; anything else ends it.
            if (isUsefulQuery) {
                var finalBody = subQuery;
                while (finalBody.Continuation != null) finalBody = finalBody.Continuation.Body;
                if (GetAnonSelectMemberNamesOrNull(finalBody) is { } names) {
                    anonMembersInScope = names;
                } else if (!finalBody.SelectOrGroup.HasAnnotation(DefaultSelectAnnotation)) {
                    anonMembersInScope = null;
                }
            }

            if (queryEnd is not null) {
                query = await ConvertQueryToLinqAsync(reusableFromCsId, queryEnd, query);
            }
            // If this segment ended with a single-item VB Select that renamed
            // the range variable (`Select sl.WarehouseLocationID` gives implicit
            // name `WarehouseLocationID`; `Select x = ...` gives `x`), the next
            // segment's downstream clauses reference the new name. Rebind the
            // outer `from` variable so `Where WarehouseLocationID.HasValue`
            // resolves — otherwise emission is `from sl in ... where
            // WarehouseLocationID.HasValue` and CS0103 "name does not exist".
            //
            // We look at the SEGMENT's own last VB clause (via querySectionsReversed's
            // last enqueued clauseEnd), which is where a Select would live for
            // this pattern (Select-forced-continuation sets `queryEnd` to null).
            if (lastVbClauseInSegment is VBSyntax.SelectClauseSyntax singleSelect
                && singleSelect.Variables.Count == 1) {
                var renamed = singleSelect.Variables[0].NameEquals?.Identifier.Identifier
                              ?? singleSelect.Variables[0].Expression.ExtractAnonymousTypeMemberName();
                if (renamed is { } renamedToken) {
                    reusableFromCsId = CommonConversions.ConvertIdentifier(renamedToken).WithoutSourceMapping();
                }
            } else if (_lastImplicitSelectSingleName is { } singleLiveName
                       && singleLiveName != reusableFromCsId.ValueText) {
                // A mid-segment VB Select (converted to a let) replaced the
                // element with one named value and the implicit segment end
                // selected it — the next segment's clauses reference that name.
                reusableFromCsId = SyntaxFactory.Identifier(singleLiveName).WithoutSourceMapping();
            }
            _lastImplicitSelectSingleName = null;
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

    private async Task<CSSyntax.QueryBodySyntax> ConvertQueryWithContinuationAsync(Queue<QuerySection> querySectionsReversed, SyntaxToken reusableCsFromId, IReadOnlyList<string> seedLiveNames)
    {
        if (!querySectionsReversed.Any()) return null;
        var section = querySectionsReversed.Dequeue();
        var (liveNames, liveChanged) = AdvanceLiveNames(seedLiveNames, section.VbClauses);
        var liveAfterEnd = section.ClauseEnd is null ? (liveNames, liveChanged) : AdvanceLiveNames(liveNames, new[] { section.ClauseEnd });
        var nestedClause = await ConvertQueryWithContinuationAsync(querySectionsReversed, reusableCsFromId, liveAfterEnd.Item1);
        var convertSubQueryAsync = await ConvertSubQueryAsync(reusableCsFromId, section.ClauseEnd, nestedClause, section.ConvertedClauses, liveNames, liveChanged);
        return convertSubQueryAsync;
    }

    /// <summary>
    /// When the last processed implicit select reduced to a single non-fromvar
    /// live name (a mid-segment VB Select converted to a let), the next
    /// segment's range variable must take that name. Communicated via this
    /// field because the recursion doesn't return shape info.
    /// </summary>
    private string _lastImplicitSelectSingleName;

    private async Task<CSSyntax.QueryBodySyntax> ConvertSubQueryAsync(SyntaxToken reusableCsFromId, VBSyntax.QueryClauseSyntax clauseEnd,
        CSSyntax.QueryBodySyntax nestedClause, SyntaxList<CSSyntax.QueryClauseSyntax> convertedClauses, IReadOnlyList<string> liveNames, bool liveChanged)
    {
        CSSyntax.SelectOrGroupClauseSyntax selectOrGroup;
        CSSyntax.QueryContinuationSyntax queryContinuation = null;
        switch (clauseEnd) {
            case null:
                // A VB query with no explicit Select yields its transparent-
                // identifier shape, tracked through the clauses by
                // AdvanceLiveNames: From/Join/Let EXTEND the element, a
                // mid-query Select (even one converted to a let) REPLACES it.
                // Emit `select new { a, b }` for a multi-name shape,
                // `select b` when a Select narrowed to one name, and the
                // pass-through `select <fromvar>` when nothing changed.
                if (liveChanged && liveNames.Count > 1) {
                    var members = liveNames.Select(n =>
                        SyntaxFactory.AnonymousObjectMemberDeclarator(ValidSyntaxFactory.IdentifierName(n)));
                    var anon = SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(members));
                    selectOrGroup = SyntaxFactory.SelectClause(anon);
                } else if (liveChanged && liveNames.Count == 1 && liveNames[0] != reusableCsFromId.ValueText) {
                    selectOrGroup = SyntaxFactory.SelectClause(ValidSyntaxFactory.IdentifierName(liveNames[0]));
                    _lastImplicitSelectSingleName = liveNames[0];
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
                    } else if (groupKeyIds.Count > 1) {
                        // Composite key `Group By oipi.StockItem, oipi.WarehouseLocation`
                        // — the C# Key is an anonymous type. Downstream code references
                        // bare `StockItem` / `WarehouseLocation` (VB transparent
                        // identifier), but C# needs `@group.Key.StockItem` etc.
                        // Emit `let StockItem = @group.Key.StockItem` for each
                        // implicitly-named key so bare references resolve.
                        // Without this, downstream `select new X(StockItem, ...)`
                        // treats `StockItem` as a type name → CS0119.
                        foreach (var keyName in groupKeyIds) {
                            var letCompositeKey = SyntaxFactory.LetClause(keyName,
                                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        ValidSyntaxFactory.IdentifierName(groupIdentifierForLet),
                                        ValidSyntaxFactory.IdentifierName("Key")),
                                    ValidSyntaxFactory.IdentifierName(keyName)));
                            continuationClauses = continuationClauses.Add(letCompositeKey);
                        }
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
                        CSSyntax.ExpressionSyntax aggExpr;
                        switch (agg.Aggregation) {
                            case VBSyntax.GroupAggregationSyntax:
                                aggExpr = ValidSyntaxFactory.IdentifierName(groupIdentifierForLet);
                                break;
                            case VBSyntax.FunctionAggregationSyntax fa: {
                                var invocationTarget = SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ValidSyntaxFactory.IdentifierName(groupIdentifierForLet),
                                    ValidSyntaxFactory.IdentifierName(fa.FunctionName.Text));
                                if (fa.Argument != null) {
                                    // VB `Into Total = Sum(x.Qty)` — the aggregation arg
                                    // is evaluated per group element. Emit `Group.Sum(x =>
                                    // x.Qty)`. Without the lambda arg, `Group.Sum()` on
                                    // IGrouping<K, T> where T isn't numeric fires CS1929.
                                    // Same as CreateGroupByProjectionAsync's arg handling,
                                    // but here in the let-emission path.
                                    var argBody = await fa.Argument.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
                                    if (liveNames.Count > 1) {
                                        // Multi-var group element `{sl, Amount}` —
                                        // the arg's bare references need
                                        // qualifying with the lambda parameter.
                                        argBody = (CSSyntax.ExpressionSyntax)new QualifyAnonMembersRewriter(reusableCsFromId.ValueText, liveNames).Visit(argBody);
                                    }
                                    var lambdaParam = SyntaxFactory.Parameter(reusableCsFromId);
                                    var lambda = SyntaxFactory.SimpleLambdaExpression(lambdaParam, argBody);
                                    aggExpr = SyntaxFactory.InvocationExpression(invocationTarget,
                                        SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda))));
                                } else {
                                    aggExpr = SyntaxFactory.InvocationExpression(invocationTarget);
                                }
                                break;
                            }
                            default:
                                aggExpr = ValidSyntaxFactory.IdentifierName(groupIdentifierForLet);
                                break;
                        }
                        continuationClauses = continuationClauses.Add(SyntaxFactory.LetClause(aggName.Text, aggExpr));
                    }
                } else if (groupKeyIds.Count == 1) {
                    var letGroupKey = SyntaxFactory.LetClause(groupKeyIds.First(), SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ValidSyntaxFactory.IdentifierName(groupIdentifierForLet), ValidSyntaxFactory.IdentifierName("Key")));
                    continuationClauses = continuationClauses.Add(letGroupKey);
                }
                if (!gcs.Items.Any()) {
                    // VB `Group By key Into Group` with no Items groups the
                    // TRANSPARENT element — when several range variables are
                    // live (`From po ... Join r ...`), the group's elements
                    // are the {po, r} pairs and downstream code accesses
                    // `l.po.X`. Grouping just the from-variable drops the
                    // rest (CS1061).
                    CSSyntax.ExpressionSyntax groupElement = liveNames.Count > 1
                        ? SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(
                            liveNames.Select(n => SyntaxFactory.AnonymousObjectMemberDeclarator(ValidSyntaxFactory.IdentifierName(n)))))
                        : ValidSyntaxFactory.IdentifierName(reusableCsFromId);
                    selectOrGroup = SyntaxFactory.GroupClause(groupElement, await GetGroupExpressionAsync(gcs));
                } else {
                    var item = await gcs.Items.Single().Expression.AcceptAsync<CSSyntax.IdentifierNameSyntax>(_triviaConvertingVisitor);
                    // COMPOSITE keys are legal here too (`Group l By a, t = f(x) Into ...`);
                    // Keys.Single() threw "Sequence contains more than one element" and
                    // aborted the whole query's conversion. GetGroupExpressionAsync builds
                    // the anonymous-type key when there are several, exactly as the
                    // implicit-item branch above already does.
                    var keyExpression = await GetGroupExpressionAsync(gcs);
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
                    var projectionSelect = await CreateGroupByProjectionAsync(gcs, GetGroupIdentifier(gcs), reusableCsFromId, liveNames);
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
            // The VB query has no explicit Select after `Group By keys Into
            // aggs`, so its element type is the anonymous shape `{keys, aggs}`
            // — downstream code accesses those members by name. `select
            // <group>` would instead surface a bare IGrouping with neither
            // member (CS1061). Project the let-bound names back into that
            // shape. Falls back to `select <group>` when a key can't be named.
            queryBody = queryBody.WithSelectOrGroup(
                CreateImplicitGroupResultProjectionOrNull(gcs, groupName) ?? CreateDefaultSelectClause(groupName));
        }
        return SyntaxFactory.QueryContinuation(groupName, queryBody);
    }

    private CSSyntax.SelectClauseSyntax CreateImplicitGroupResultProjectionOrNull(VBSyntax.GroupByClauseSyntax gcs, SyntaxToken groupName)
    {
        var keyIds = GetGroupKeyIdentifiers(gcs).ToList();
        // Unnameable key expression or no aggregations — leave the old
        // `select <group>` shape rather than emit an incomplete projection.
        if (keyIds.Count != gcs.Keys.Count || !gcs.AggregationVariables.Any()) return null;

        var members = new List<CSSyntax.AnonymousObjectMemberDeclaratorSyntax>();
        foreach (var keyId in keyIds) {
            // let-bound earlier in the continuation clauses
            members.Add(SyntaxFactory.AnonymousObjectMemberDeclarator(ValidSyntaxFactory.IdentifierName(keyId)));
        }
        foreach (var agg in gcs.AggregationVariables) {
            var vbName = agg.NameEquals?.Identifier.Identifier.Text
                         ?? (agg.Aggregation is VBSyntax.FunctionAggregationSyntax fnAgg ? fnAgg.FunctionName.Text
                             : agg.Aggregation is VBSyntax.GroupAggregationSyntax ? "Group" : null);
            if (vbName == null) return null;
            if (agg.Aggregation is VBSyntax.GroupAggregationSyntax) {
                // The group itself — reference the continuation variable. Use
                // an explicit name when the identifier differs (e.g. `@group`
                // fallback) so the member keeps its VB name.
                members.Add(string.Equals(vbName, groupName.ValueText, StringComparison.Ordinal)
                    ? SyntaxFactory.AnonymousObjectMemberDeclarator(ValidSyntaxFactory.IdentifierName(groupName.Text))
                    : SyntaxFactory.AnonymousObjectMemberDeclarator(
                        SyntaxFactory.NameEquals(ValidSyntaxFactory.IdentifierName(vbName)),
                        ValidSyntaxFactory.IdentifierName(groupName.Text)));
            } else {
                // Function aggregations were let-bound under this name.
                members.Add(SyntaxFactory.AnonymousObjectMemberDeclarator(ValidSyntaxFactory.IdentifierName(vbName)));
            }
        }
        return SyntaxFactory.SelectClause(
            SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(members)));
    }

    private async Task<IEnumerable<CSSyntax.ExpressionSyntax>> GetLinqArgumentsAsync(SyntaxToken reusableCsFromId,
        VBSyntax.QueryClauseSyntax linqQuery)
    {
        switch (linqQuery) {
            case VBSyntax.DistinctClauseSyntax _:
                return Enumerable.Empty<CSSyntax.ExpressionSyntax>();
            case VBSyntax.PartitionClauseSyntax pcs: {
                // `Take TotalRows` / `Skip SkipAmount` are query CLAUSES, so the count
                // never passes through VisitSimpleArgument and got no conversion at
                // all. Under Option Strict Off the count is routinely something VB
                // narrows implicitly — `Integer?`, `Double` — and Enumerable.Skip/Take
                // take an int, so it emitted CS1503.
                var count = await pcs.Count.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
                count = CommonConversions.TypeConversionAnalyzer.AddExplicitConversion(
                    pcs.Count, count,
                    forceTargetType: _semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32));
                return new[] { count };
            }
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
                                                                                                                || queryClauseSyntax is VBSyntax.SelectClauseSyntax sc && !CanEmitSelectAsLets(sc, clausesAfter) && (sc.Variables.Any(v => v.NameEquals is null) || clausesAfter == 0);

    /// <summary>
    /// VB `Select x, Extra1 = ..., Extra2 = ...` where one variable is a bare
    /// range-variable reference: VB's transparent identifier machinery keeps
    /// `x.Member` accessible in downstream clauses. C# `select new {x, Extra1,
    /// Extra2}` (the default multi-var emission) loses that — the range
    /// variable becomes the anon type and `x.Member` fails with CS1061.
    ///
    /// When exactly one item is a bare IdentifierName we can preserve the range
    /// variable and emit `let` clauses for each other var instead, keeping the
    /// current segment (no continuation) and letting subsequent clauses see
    /// both `x` and the new lets. The bare identifier can appear in any
    /// position (VB `Select foo, bar` doesn't imply order — both are members
    /// of the anon type).
    /// </summary>
    private static bool CanEmitSelectAsLets(VBSyntax.SelectClauseSyntax sc, int clausesAfter)
    {
        // Only intercept mid-query Selects (there are downstream clauses).
        // The FINAL Select becomes the query's output — anon-type projection
        // is the right emission there, and we can't reliably tell a bare
        // identifier apart from a let-bound name at that point.
        if (clausesAfter == 0) return false;
        if (sc.Variables.Count < 2) return false;
        int bareCount = 0;
        string bareName = null;
        foreach (var v in sc.Variables) {
            bool isBareIdentifier = v.NameEquals == null && v.Expression is VBSyntax.IdentifierNameSyntax;
            if (isBareIdentifier) {
                bareCount++;
                bareName = ((VBSyntax.IdentifierNameSyntax)v.Expression).Identifier.ValueText;
                continue;
            }
            // Non-bare vars need a name we can lift into a `let`.
            if (v.NameEquals == null && v.Expression.ExtractAnonymousTypeMemberName() == null) return false;
        }
        // Exactly one bare identifier (the presumed range variable) —
        // otherwise we can't tell which is the range var to preserve.
        if (bareCount != 1 || bareName == null) return false;

        // Downstream-safety check: VB `Select oa, oa.Order, Weight = ...`
        // creates an anon type with `oa` AS A MEMBER; downstream code may
        // access `oa.oa` (VB transparent-identifier reference to the
        // OrderAction sub-member). If we let-emit and preserve `oa` as the
        // OrderAction range variable directly, `oa.oa` no longer resolves
        // (CS1061 "does not contain a definition for 'oa'").
        //
        // Skip the transform when the ENCLOSING method contains any
        // `<bareName>.<bareName>` pattern — that's a strong signal downstream
        // code depends on the anon-type projection shape.
        VBasic.VisualBasicSyntaxNode enclosing = sc.FirstAncestorOrSelf<VBSyntax.MethodBlockSyntax>();
        enclosing ??= sc.FirstAncestorOrSelf<VBSyntax.MultiLineLambdaExpressionSyntax>();
        enclosing ??= sc.FirstAncestorOrSelf<VBSyntax.SingleLineLambdaExpressionSyntax>();
        enclosing ??= sc.FirstAncestorOrSelf<VBSyntax.PropertyBlockSyntax>();
        if (enclosing != null) {
            foreach (var maes in enclosing.DescendantNodes().OfType<VBSyntax.MemberAccessExpressionSyntax>()) {
                if (maes.Expression is VBSyntax.IdentifierNameSyntax exprId
                    && exprId.Identifier.ValueText == bareName
                    && maes.Name is VBSyntax.IdentifierNameSyntax nameId
                    && nameId.Identifier.ValueText == bareName) {
                    return false;
                }
            }
        }
        return true;
    }

    private async Task<IEnumerable<CSSyntax.FromClauseSyntax>> ConvertFromClauseSyntaxAsync(VBSyntax.FromClauseSyntax vbFromClause) => await vbFromClause.Variables.SelectAsync(ConvertFromClauseVariableAsync);

    private async Task<CSSyntax.FromClauseSyntax> ConvertFromClauseVariableAsync(CollectionRangeVariableSyntax collectionRangeVariableSyntax)
    {
        var expression = await collectionRangeVariableSyntax.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
        var parentOperation = _semanticModel.GetOperation(collectionRangeVariableSyntax.Expression)?.Parent;
        if (parentOperation != null && parentOperation.IsImplicit && parentOperation is IInvocationOperation io &&
            io.TargetMethod.MethodKind == MethodKind.ReducedExtension && io.TargetMethod.Name == nameof(Enumerable.AsEnumerable)) {
            expression = SyntaxFactory.InvocationExpression(ValidSyntaxFactory.MemberAccess(expression, io.TargetMethod.Name), SyntaxFactory.ArgumentList());
        }
        // VB `From dr As DataRow In dt.Rows` — the explicit range-variable
        // type matters when the source is a non-generic IEnumerable (VB
        // inserts an implicit cast). C# has the same construct: `from DataRow
        // dr in dt.Rows` (compiles to Cast<DataRow>()). Dropping it fails
        // with CS1934 "could not find an implementation of the query pattern".
        CSSyntax.TypeSyntax rangeVarType = null;
        if (collectionRangeVariableSyntax.AsClause is VBSyntax.SimpleAsClauseSyntax asClause) {
            rangeVarType = await asClause.Type.AcceptAsync<CSSyntax.TypeSyntax>(_triviaConvertingVisitor);
        }
        var fromClauseSyntax = SyntaxFactory.FromClause(
            rangeVarType,
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
            // When we get here via segmentation the Select was NOT a segment
            // boundary, so it's guaranteed to have downstream clauses in the
            // same segment — safe to pass a non-zero clausesAfter marker.
            VBSyntax.SelectClauseSyntax x when CanEmitSelectAsLets(x, clausesAfter: 1) => await ConvertSelectWithRetainedRangeVarAsLetsAsync(x),
            VBSyntax.SelectClauseSyntax x => await ConvertSelectClauseAsync(x).YieldAsync(),
            VBSyntax.LetClauseSyntax x => await ConvertLetClauseAsync(x).YieldAsync(),
            VBSyntax.OrderByClauseSyntax x => await ConvertOrderByClauseAsync(x).YieldAsync(),
            VBSyntax.WhereClauseSyntax x => await ConvertWhereClauseAsync(x).YieldAsync(),
            _ => throw new NotImplementedException($"Conversion for query clause with kind '{node.Kind()}' not implemented")
        };
    }

    private async Task<IEnumerable<CSSyntax.QueryClauseSyntax>> ConvertSelectWithRetainedRangeVarAsLetsAsync(VBSyntax.SelectClauseSyntax sc)
    {
        // Skip the bare-identifier variable (the presumed range var —
        // guaranteed to be exactly one by CanEmitSelectAsLets). Emit one
        // `let <name> = <expr>` per non-bare variable.
        var clauses = new List<CSSyntax.QueryClauseSyntax>();
        foreach (var v in sc.Variables) {
            bool isBareIdentifier = v.NameEquals == null && v.Expression is VBSyntax.IdentifierNameSyntax;
            if (isBareIdentifier) continue;
            var nameToken = v.NameEquals?.Identifier.Identifier
                            ?? v.Expression.ExtractAnonymousTypeMemberName().Value;
            var expression = await v.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
            clauses.Add(SyntaxFactory.LetClause(CommonConversions.ConvertIdentifier(nameToken), expression));
        }
        return clauses;
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

    private async Task<CSSyntax.SelectClauseSyntax> CreateGroupByProjectionAsync(VBSyntax.GroupByClauseSyntax gcs, SyntaxToken groupName, SyntaxToken rangeVariableName, IReadOnlyList<string> liveNames)
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
                    // VB `Into Value = Sum(ss.Value)` — the aggregation argument
                    // is evaluated per-element of the group, so the C# form is
                    // `Group.Sum(ss => ss.Value)` (lambda over the outer range
                    // variable). Without the lambda we'd emit bare `Group.Sum()`
                    // on IGrouping<K,T> where T isn't numeric — CS1929.
                    if (fa.Argument != null) {
                        var argBody = await fa.Argument.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
                        if (liveNames.Count > 1) {
                            // Multi-var group element — qualify bare references
                            // with the lambda parameter.
                            argBody = (CSSyntax.ExpressionSyntax)new QualifyAnonMembersRewriter(rangeVariableName.ValueText, liveNames).Visit(argBody);
                        }
                        var lambdaParam = SyntaxFactory.Parameter(rangeVariableName);
                        var lambda = SyntaxFactory.SimpleLambdaExpression(lambdaParam, argBody);
                        aggExpr = SyntaxFactory.InvocationExpression(invocationTarget,
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda))));
                    } else {
                        aggExpr = SyntaxFactory.InvocationExpression(invocationTarget);
                    }
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
        var groupExpressions = (await gs.Keys.SelectAsync(async k => (name: k.NameEquals?.Identifier.Identifier.Text, vb: k.Expression, cs: await k.Expression.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor)))).ToList();
        return (groupExpressions.Count == 1) ? groupExpressions.Single().cs : CreateAnonymousType(groupExpressions);
    }

    private static CSSyntax.ExpressionSyntax CreateAnonymousType(List<(string name, ExpressionSyntax vb, CSSyntax.ExpressionSyntax cs)> groupExpressions)
    {
        return SyntaxFactory.AnonymousObjectCreationExpression(SyntaxFactory.SeparatedList(groupExpressions.Select(CreateAnonymousMember)));
    }

    private static CSSyntax.AnonymousObjectMemberDeclaratorSyntax CreateAnonymousMember((string name, ExpressionSyntax vb, CSSyntax.ExpressionSyntax cs) expr, int i)
    {
        // An explicit VB key name (`Group By LocationGuid = sl.Location.LinnworksGUID`)
        // takes priority — the projection and downstream code reference the key
        // by that name, not by the trailing member of the expression.
        var name = SyntaxFactory.Identifier(expr.name ?? expr.vb.ExtractAnonymousTypeMemberName()?.Text ?? ("key" + i));
        return SyntaxFactory.AnonymousObjectMemberDeclarator(SyntaxFactory.NameEquals(ValidSyntaxFactory.IdentifierName(name)), expr.cs);
    }

    private SyntaxToken GetGroupIdentifier(VBSyntax.GroupByClauseSyntax gs)
    {
        // Compute the set of names we'll subsequently let-bind (see the
        // GroupByClauseSyntax branch in ConvertSubQueryAsync). Using any of
        // these as the `into` identifier would produce
        // `into Group let Group = Group.Key` (CS1930 "range variable already
        // declared").
        var letBoundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in gs.Keys) {
            if (k.NameEquals?.Identifier.Identifier is { } n) letBoundNames.Add(n.ValueText);
            else if (k.Expression.ExtractAnonymousTypeMemberName() is { } n2) letBoundNames.Add(n2.ValueText);
        }
        foreach (var v in gs.AggregationVariables) {
            if (v.NameEquals?.Identifier.Identifier is { } n) letBoundNames.Add(n.ValueText);
            else if (v.Aggregation is VBSyntax.FunctionAggregationSyntax fn) letBoundNames.Add(fn.FunctionName.ValueText);
        }
        // No Items: preserves the old default of `Group`, but fall back to
        // `@group` on collision (e.g. VB `Group By Group = wd.Date_ Into ...`
        // — where `Group` IS a key name we'll let-bind to `.Key`).
        if (!gs.Items.Any()) {
            return letBoundNames.Contains("Group")
                ? SyntaxFactory.Identifier("@group")
                : CommonConversions.CsEscapedIdentifier("Group");
        }
        var name = gs.AggregationVariables.Select(v => v.Aggregation switch {
            VBSyntax.FunctionAggregationSyntax f => f.FunctionName,
            VBSyntax.GroupAggregationSyntax => v.NameEquals?.Identifier.Identifier,
            _ => default
        }).Concat(gs.Keys.Select(k => k.NameEquals?.Identifier.Identifier)).FirstOrDefault(x => x != null);
        if (name is {} finalName) return CommonConversions.ConvertIdentifier(finalName);
        // `Group d By d.Picker Into Group` (items present, aggregation unnamed)
        // declares a range variable literally called `Group`, and the query body
        // refers to it by that name. Falling straight through to `@group` renamed
        // the DECLARATION but not the references — and because C# is
        // case-sensitive, `Group.Sum(...)` in the body then bound to whatever TYPE
        // named Group was in scope instead (CS0104, ambiguous between
        // BMCore.DataClasses.Group and System.Text.RegularExpressions.Group).
        // Same reasoning as the no-items branch above, so keep the same collision
        // guard.
        if (!letBoundNames.Contains("Group") &&
            gs.AggregationVariables.Any(v => v.Aggregation is VBSyntax.GroupAggregationSyntax && v.NameEquals == null)) {
            return CommonConversions.CsEscapedIdentifier("Group");
        }
        return SyntaxFactory.Identifier("@group");
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
        var condition = await ws.Condition.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor);
        // VB `Where r.Reason?.SomeBool` — the `?.` gives a genuine `bool?`,
        // and VB Where accepts it via nullable Boolean semantics (Nothing →
        // filter out). C# `where` requires `bool` (CS0266). Append `?? false`
        // to preserve the semantics.
        //
        // Restricted to conditional-access conditions: relational and equality
        // ops with nullable operands look nullable to VB's semantic model but
        // C#'s lifted operator returns `bool` directly, so appending
        // `?? false` there would produce `bool ?? false` — CS0019.
        if (ws.Condition.SkipOutOfParens() is VBSyntax.ConditionalAccessExpressionSyntax) {
            var conditionType = _semanticModel.GetTypeInfo(ws.Condition).Type;
            if (conditionType != null && conditionType.IsNullable(out var underlying)
                                      && underlying?.SpecialType == SpecialType.System_Boolean) {
                condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    condition.AddParens(),
                    LiteralConversions.GetLiteralExpression(false));
            }
        }
        // VB `Where t.Role And Server.Role` — bitwise AND on flag-enums;
        // VB accepts the enum result in Where (non-zero = true, zero =
        // false). C# `where` requires `bool` (CS0029). Emit `!= 0` on the
        // enum expression, cast to the underlying type to satisfy `0`
        // literal comparison.
        var whereType = _semanticModel.GetTypeInfo(ws.Condition).Type;
        if (whereType?.TypeKind == TypeKind.Enum) {
            var underlyingEnumType = ((INamedTypeSymbol)whereType).EnumUnderlyingType;
            var typeName = (CSSyntax.TypeSyntax)CommonConversions.CsSyntaxGenerator.TypeExpression(underlyingEnumType);
            condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                ValidSyntaxFactory.CastExpression(typeName, condition.AddParens()),
                LiteralConversions.GetLiteralExpression(0));
        }
        return SyntaxFactory.WhereClause(condition);
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

        // VB unifies mismatched join key types via its usual conversions
        // (`On o.ExternalOrderID Equals r.ReplacementOrderRef` with
        // Integer/String keys). C# Join infers ONE key type and fails with
        // CS1941 — apply each key's VB conversion (Type -> ConvertedType) so
        // both sides land on the unified type.
        var joinLhsExpressions = await js.JoinConditions.SelectAsync(async c =>
            CommonConversions.TypeConversionAnalyzer.AddExplicitConversion(c.Left,
                await c.Left.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor)));

        var joinRhsExpressions = await js.JoinConditions.SelectAsync(async c =>
            CommonConversions.TypeConversionAnalyzer.AddExplicitConversion(c.Right,
                await c.Right.AcceptAsync<CSSyntax.ExpressionSyntax>(_triviaConvertingVisitor)));

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
        // C# enforces specific ordering of range variables around the equals
        // token inside a join clause (CS1937/1938): the LEFT key references
        // outer range variables, the RIGHT key references the joined
        // variable. Detect by looking for any reference to the joined
        // variable anywhere in the key — this sees through member-access
        // chains, key-type conversions (`(double)r.Ref`,
        // `Conversions.ToDouble(o.ID)`), casts and invocations alike.
        static bool ReferencesIdentifier(CSSyntax.ExpressionSyntax expr, string identifierName) =>
            expr.DescendantNodesAndSelf().OfType<CSSyntax.IdentifierNameSyntax>()
                .Any(id => id.Identifier.ValueText == identifierName
                           && (id.Parent is not CSSyntax.MemberAccessExpressionSyntax ma || ma.Expression == id));
        var swappedExpressions = expressions
            .Select(expression => ReferencesIdentifier(expression.Lhs, convertIdentifier.ValueText)
                ? SwapExpressions(expression)
                : expression)
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

    private static IReadOnlyCollection<string> GetAnonSelectMemberNamesOrNull(CSSyntax.QueryBodySyntax finalBody)
    {
        if (finalBody.SelectOrGroup is not CSSyntax.SelectClauseSyntax sel ||
            sel.Expression is not CSSyntax.AnonymousObjectCreationExpressionSyntax anon) return null;
        var names = anon.Initializers
            .Select(i => i.NameEquals?.Name.Identifier.ValueText
                         ?? (i.Expression as CSSyntax.IdentifierNameSyntax)?.Identifier.ValueText
                         ?? ((i.Expression as CSSyntax.MemberAccessExpressionSyntax)?.Name as CSSyntax.IdentifierNameSyntax)?.Identifier.ValueText)
            .Where(n => n != null)
            .ToList();
        return names.Count > 0 ? names : null;
    }

    /// <summary>
    /// Rewrites bare references to a previous segment's anonymous-select
    /// members into member accesses on the current segment's range variable
    /// (VB's transparent identifier made them look like locals). Skips scopes
    /// that redeclare a matching name (lambda parameters, nested query range
    /// variables) and the harness's own synthesized default selects, which
    /// pass the element through unchanged.
    /// </summary>
    private sealed class QualifyAnonMembersRewriter : CSharpSyntaxRewriter
    {
        private readonly string _qualifier;
        private HashSet<string> _names;

        public QualifyAnonMembersRewriter(string qualifier, IEnumerable<string> names)
        {
            _qualifier = qualifier;
            _names = new HashSet<string>(names, StringComparer.Ordinal);
        }

        public override SyntaxNode VisitIdentifierName(CSSyntax.IdentifierNameSyntax node)
        {
            if (!_names.Contains(node.Identifier.ValueText) || !IsQualifiablePosition(node)) return node;
            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                ValidSyntaxFactory.IdentifierName(_qualifier), node.WithoutTrivia()).WithTriviaFrom(node);
        }

        private static bool IsQualifiablePosition(CSSyntax.IdentifierNameSyntax node) => node.Parent switch {
            CSSyntax.MemberAccessExpressionSyntax ma when ma.Name == node => false,
            CSSyntax.NameEqualsSyntax => false,
            CSSyntax.NameColonSyntax => false,
            CSSyntax.QualifiedNameSyntax => false,
            CSSyntax.MemberBindingExpressionSyntax => false,
            // The member being INITIALISED in an object initializer is a member
            // name, not a reference to the range variable that happens to share
            // its name. Qualifying it produced `p.StockItem = p.StockItem`, which
            // is not a legal initializer target (CS0747), and the knock-on read of
            // it as an anonymous-type member assignment gave CS0200 as well.
            CSSyntax.AssignmentExpressionSyntax ae when ae.Left == node
                && ae.Parent.IsKind(SyntaxKind.ObjectInitializerExpression) => false,
            _ => true
        };

        public override SyntaxNode VisitSelectClause(CSSyntax.SelectClauseSyntax node) =>
            node.HasAnnotation(DefaultSelectAnnotation) ? node : base.VisitSelectClause(node);

        public override SyntaxNode VisitAnonymousObjectMemberDeclarator(CSSyntax.AnonymousObjectMemberDeclaratorSyntax node)
        {
            // A bare-identifier member (`new { ooi }`) that gets qualified
            // loses its inferrable name — pin it with an explicit NameEquals.
            if (node.NameEquals == null && node.Expression is CSSyntax.IdentifierNameSyntax id && _names.Contains(id.Identifier.ValueText)) {
                node = node.WithNameEquals(SyntaxFactory.NameEquals(ValidSyntaxFactory.IdentifierName(id.Identifier.ValueText)));
            }
            return base.VisitAnonymousObjectMemberDeclarator(node);
        }

        public override SyntaxNode VisitSimpleLambdaExpression(CSSyntax.SimpleLambdaExpressionSyntax node) =>
            VisitWithShadowed(new[] { node.Parameter.Identifier.ValueText }, () => base.VisitSimpleLambdaExpression(node));

        public override SyntaxNode VisitParenthesizedLambdaExpression(CSSyntax.ParenthesizedLambdaExpressionSyntax node) =>
            VisitWithShadowed(node.ParameterList.Parameters.Select(p => p.Identifier.ValueText), () => base.VisitParenthesizedLambdaExpression(node));

        public override SyntaxNode VisitQueryExpression(CSSyntax.QueryExpressionSyntax node)
        {
            // A nested query redeclares its own range variables — its bare
            // references to those names must not be qualified.
            var declared = node.DescendantNodes().SelectMany(n => n switch {
                CSSyntax.FromClauseSyntax f => new[] { f.Identifier.ValueText },
                CSSyntax.LetClauseSyntax l => new[] { l.Identifier.ValueText },
                CSSyntax.JoinClauseSyntax j => new[] { (j.Into?.Identifier ?? j.Identifier).ValueText },
                CSSyntax.QueryContinuationSyntax qc => new[] { qc.Identifier.ValueText },
                _ => Array.Empty<string>()
            });
            return VisitWithShadowed(declared, () => base.VisitQueryExpression(node));
        }

        private SyntaxNode VisitWithShadowed(IEnumerable<string> shadowed, Func<SyntaxNode> visit)
        {
            var saved = _names;
            _names = new HashSet<string>(_names.Except(shadowed), StringComparer.Ordinal);
            try {
                return visit();
            } finally {
                _names = saved;
            }
        }
    }
}