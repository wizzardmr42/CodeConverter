namespace ICSharpCode.CodeConverter.CSharp;

internal class TypeContext : ITypeContext
{
    private readonly Stack<(AdditionalInitializers Initializers, HandledEventsAnalysis Methods, GeneratedAnonymousTypes AnonymousTypes)> _contextStack = new();

    public AdditionalInitializers Initializers => _contextStack.Peek().Initializers;
    public HandledEventsAnalysis HandledEventsAnalysis => _contextStack.Peek().Methods;
    public GeneratedAnonymousTypes GeneratedAnonymousTypes => _contextStack.Peek().AnonymousTypes;

    public PerScopeState PerScopeState { get; internal set; } = new();

    public void Push(HandledEventsAnalysis methodWithHandles, AdditionalInitializers additionalInitializers)
    {
        _contextStack.Push((additionalInitializers, methodWithHandles, new GeneratedAnonymousTypes()));
    }

    public void Pop() => _contextStack.Pop();
    public bool Any() => _contextStack.Count > 0;
}