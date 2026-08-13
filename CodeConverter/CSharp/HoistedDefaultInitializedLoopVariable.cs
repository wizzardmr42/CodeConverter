using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ICSharpCode.CodeConverter.CSharp;

internal class HoistedDefaultInitializedLoopVariable : IHoistedNode
{
    public string OriginalVariableName { get; }
    public string Id { get; }
    public ExpressionSyntax Initializer { get; }
    public TypeSyntax Type { get; }
    public bool Nested { get; }

    /// <summary>
    /// The name was already uniquified at hoist time (renamed to avoid a CS0136
    /// collision) and registered in the generated-names set — don't uniquify again.
    /// </summary>
    public bool AlreadyUnique { get; }

    public HoistedDefaultInitializedLoopVariable(string originalVariableName, ExpressionSyntax initializer, TypeSyntax type, bool nested, bool alreadyUnique = false)
    {
        OriginalVariableName = originalVariableName;
        Id = $"ph{Guid.NewGuid():N}";
        Initializer = initializer;
        Type = type;
        Nested = nested;
        AlreadyUnique = alreadyUnique;
    }

}