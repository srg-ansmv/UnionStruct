using System.Collections.Immutable;
using System.Linq;
using System.Text;
using UnionStruct.Unions;

namespace UnionStruct.CodeGeneration;

public static class UnionCodeGenerator
{
    public static string GenerateUnionPartialImplementation(UnionDescriptor descriptor)
    {
        var unionContext = UnionContext.Create(descriptor);

        var generatedEnum = EnumDeclarationGenerator.Generate(unionContext);
        var generatedInits = InitializationGenerator.Generate(unionContext);
        var generatedChecks = CheckGenerators.Generate(unionContext, generatedEnum);

        var structDeclaration = $"public readonly partial struct {unionContext.FullUnionDeclaration}";

        var foldMethodDeclaration = GenerateFold(unionContext);
        var mapMethodsDeclaration = GenerateMapMethods(unionContext);

        var namespaceDeclaration = $"namespace {descriptor.Namespace ?? $"UnionStruct.Generated.{descriptor.StructName}"};";

        var usingsDeclaration = string.Join("\n", descriptor.Usings.Select(x => $"using {x};").Concat([
            "using System.Diagnostics.CodeAnalysis;",
            "using System.Runtime.InteropServices;",
            "using System.Threading.Tasks;",
            "using System;",
        ]));

        const string nullableDeclaration = "#nullable enable";
        const string structAttributes = $"[StructLayout(LayoutKind.Auto)]\n{GeneratedCodeAttributeLine.Code}";

        var extensionsCode = ExtensionsGenerator.Generate(unionContext);

        return usingsDeclaration + "\n\n"
                                 + nullableDeclaration + "\n\n"
                                 + namespaceDeclaration + "\n\n"
                                 + generatedEnum.Body + "\n\n"
                                 + structAttributes + "\n"
                                 + structDeclaration + "\n"
                                 + "{" + "\n"
                                 + generatedInits.Body + "\n"
                                 + generatedEnum.EnumPropertyDeclaration + "\n"
                                 + generatedChecks.Body + "\n"
                                 + foldMethodDeclaration + "\n"
                                 + mapMethodsDeclaration + "\n"
                                 + "}" + "\n"
                                 + extensionsCode;
    }

    private static string GenerateMapMethods(UnionContext context)
    {
        var descriptors = context.Descriptor.Fields;
        var stateEnumMap = context.FieldNameToEnumMap;
        var unvaluedEnums = context.UnvaluedEnums;
        var structName = context.Descriptor.StructName;
        var genericParams = context.Descriptor.GenericParameters;
        
        var sb = new StringBuilder();

        var query = descriptors
            .Where(x => x.UnionArguments.TryGetValue(nameof(UnionPartAttribute.AddMap), out var value) && value == "true")
            .Select((x, i) => (x, i, genericParams.Contains(x.Type)));

        foreach (var (descriptor, index, isGeneric) in query)
        {
            var stateName = stateEnumMap[descriptor.Name];
            
            var genericPlaceHolders = string.Join(",", Enumerable.Range(0, genericParams.Length).Select(x => $"{{{x}}}"));
            var genericFormat = $"<{genericPlaceHolders}>";
            var outcomeParams = string.Format(genericFormat,
                genericParams.Select<string, object>((x, i) => isGeneric && i == index ? "TOut" : x).ToArray());

            var newStructType = $"{structName}{(outcomeParams == "<>" ? string.Empty : outcomeParams)}";
            var funcType = isGeneric ? "TOut" : descriptor.Type;
            var genericType = isGeneric ? "<TOut>" : string.Empty;
            
            var genericConstraint =
                isGeneric && context.Descriptor.GenericConstraints.TryGetValue(descriptor.Type, out var constraint)
                    ? constraint.Replace(descriptor.Type, "TOut")
                    : string.Empty;

            var restSwitch = string.Join('\n', descriptors
                .Where(x => x.Name != descriptor.Name)
                .Select(x =>
                    $"_ when Is{stateEnumMap[x.Name]}(out var x) =>{newStructType}.{stateEnumMap[x.Name]}(x{(x.IsNullable ? ".Value" : string.Empty)}),")
                .Concat(unvaluedEnums.Select(x => $"_ when Is{x}() => {newStructType}.{x},"))
                .Append("_ => throw new NotImplementedException()"));

            
            sb.AppendLine(
                $$"""
                  public {{newStructType}} Map{{stateName}}{{genericType}}(Func<{{descriptor.Type}}, {{funcType}}> mapper) {{genericConstraint}} => this switch
                  {
                      _ when Is{{stateName}}(out var x) => {{newStructType}}.{{stateName}}(mapper(x{{(descriptor.IsNullable ? ".Value" : string.Empty)}})),
                      {{restSwitch}}
                  };
                  """
            );

            sb.AppendLine(
                $$"""
                  public {{newStructType}} Map{{stateName}}{{genericType}}(Func<{{descriptor.Type}}, {{newStructType}}> mapper) {{genericConstraint}} => this switch
                  {
                      _ when Is{{stateName}}(out var x) => mapper(x{{(descriptor.IsNullable ? ".Value" : string.Empty)}}),
                      {{restSwitch}}
                  };
                  """
            );

            var asyncRestSwitch = string.Join('\n', descriptors
                .Where(x => x.Name != descriptor.Name)
                .Select(x =>
                    $"_ when Is{stateEnumMap[x.Name]}(out var x) => Task.FromResult({newStructType}.{stateEnumMap[x.Name]}(x{(x.IsNullable ? ".Value" : string.Empty)})),")
                .Concat(unvaluedEnums.Select(x => $"_ when Is{x}() => Task.FromResult({newStructType}.{x}),"))
                .Append($"_ => Task.FromException<{newStructType}>(new NotImplementedException())"));

            sb.AppendLine(
                $$"""
                  public Task<{{newStructType}}> Map{{stateName}}Async{{genericType}}(Func<{{descriptor.Type}}, Task<{{funcType}}>> mapper) {{genericConstraint}} => this switch 
                  {
                    _ when Is{{stateName}}(out var x) => mapper(x{{(descriptor.IsNullable ? ".Value" : string.Empty)}}).ContinueWith(
                            t => t switch 
                            { 
                                { Exception: null } => {{newStructType}}.{{stateName}}(t.Result),
                                { Exception: not null } => throw new InvalidOperationException("Error when mapping", innerException: t.Exception),
                                _ => throw new InvalidOperationException("Error when mapping") 
                            }
                        ),
                    {{asyncRestSwitch}}
                  };
                  """
            );

            sb.AppendLine(
                $$"""
                  public Task<{{newStructType}}> Map{{stateName}}Async{{genericType}}(Func<{{descriptor.Type}}, Task<{{newStructType}}>> mapper) {{genericConstraint}} => this switch 
                  {
                    _ when Is{{stateName}}(out var x) => mapper(x{{(descriptor.IsNullable ? ".Value" : string.Empty)}}).ContinueWith(
                            t => t switch 
                            { 
                                { Exception: null } => t.Result,
                                { Exception: not null } => throw new InvalidOperationException("Error when mapping", innerException: t.Exception),
                                _ => throw new InvalidOperationException("Error when mapping") 
                            }
                        ),
                    {{asyncRestSwitch}}
                  };
                  """
            );
        }

        return sb.ToString();
    }

    private static string GenerateFold(UnionContext unionContext)
    {
        var descriptors = unionContext.Descriptor.Fields;
        var stateEnumMap = unionContext.FieldNameToEnumMap;
        var unvaluedEnums = unionContext.UnvaluedEnums;
        
        var funcParameters = string.Join(',',
            descriptors.Select(x => $"Func<{x.Type}, TOut> {stateEnumMap[x.Name]}")
                .Concat(unvaluedEnums.Select(x => $"Func<TOut> {x}"))
        );

        var funcAsyncParameters = string.Join(',',
            descriptors.Select(x => $"Func<{x.Type}, Task<TOut>> {stateEnumMap[x.Name]}")
                .Concat(unvaluedEnums.Select(x => $"Func<Task<TOut>> {x}"))
        );

        var switchParts = string.Join('\n', descriptors
            .Select(x => $"_ when Is{stateEnumMap[x.Name]}(out var x) => {stateEnumMap[x.Name]}(x{(x.IsNullable ? ".Value" : string.Empty)}),")
            .Concat(unvaluedEnums.Select(x => $"_ when Is{x}() => {x}(),"))
            .Append("_ => throw new NotImplementedException()"));

        return $$"""
                 public TOut Fold<TOut>({{funcParameters}}) => this switch 
                 {
                    {{switchParts}}
                 };

                 public Task<TOut> FoldAsync<TOut>({{funcAsyncParameters}}) => this switch
                 {
                    {{switchParts}}
                 };
                 """;
    }
}