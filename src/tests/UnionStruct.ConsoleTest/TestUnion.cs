using UnionStruct.Unions;

namespace UnionStruct.ConsoleTest;

[Union]
public readonly partial struct TestUnion<T>
{
    [UnionPart(AddMap = true)] private readonly T? _genericState;
    [UnionPart(AddMap = true)] private readonly IState2? _state2;
}

public interface IState2;