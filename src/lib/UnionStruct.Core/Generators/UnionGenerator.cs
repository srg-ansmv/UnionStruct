using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnionStruct.CodeGeneration;
using UnionStruct.Unions;

namespace UnionStruct.Generators;

[Generator]
public class UnionGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var types = context.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes()).OfType<StructDeclarationSyntax>()
            .Where(tree => tree.Modifiers.Any(m => m.Text == "partial")
                           && tree.AttributeLists.SelectMany(a => a.Attributes).Any(x => x.Name.ToString() == "Union"))
            .Select(tree => new
            {
                NamespaceTree = Extensions.TraverseBack<BaseNamespaceDeclarationSyntax>(tree),
                Usings = Extensions.TraverseBack<CompilationUnitSyntax>(tree)?.Usings.Select(x => x.Name.ToFullString())
                    .ToImmutableArray(),
                StructTree = tree,
                FieldsTree = tree.DescendantNodes().OfType<FieldDeclarationSyntax>()
                    .Where(field =>
                        field.AttributeLists.SelectMany(a => a.Attributes).Any(x => x.Name.ToString() == "UnionPart"))
                    .ToList()
            })
            .Select(trees =>
            {
                var constraint = trees.StructTree.DescendantNodes().OfType<TypeParameterConstraintClauseSyntax>();
                var genericConstraints = constraint.ToImmutableDictionary(x => x.Name.ToString(), x => x.ToString());

                var genericParameters = trees.StructTree.DescendantNodes().OfType<TypeParameterSyntax>()
                    .Where(x => Extensions.TraverseBack<MethodDeclarationSyntax>(x) is null)
                    .Select(x => x.Identifier.ToString())
                    .Distinct()
                    .ToImmutableArray();

                var fields = trees.FieldsTree.Select(f => new UnionTypeDescriptor(
                    f.Declaration.Variables.Single().Identifier.ToString(),
                    Extensions.GetFieldType(f, context.Compilation) ?? string.Empty,
                    f.AttributeLists.SelectMany(a => a.Attributes)
                        .Where(a => a.ToString().StartsWith("UnionPart"))
                        .SelectMany(a =>
                            a.ArgumentList?.Arguments.Select(arg => Extensions.ParseArgumentSyntax(arg.ToString()))
                            ?? []
                        )
                        .ToImmutableDictionary(x => x.Argument, x => x.Value),
                    Extensions.IsStruct(f, context.Compilation) && f.Declaration.Type is NullableTypeSyntax
                )).ToImmutableArray();

                return new UnionDescriptor(
                    trees.NamespaceTree?.Name.ToString(),
                    trees.StructTree.Identifier.ToString(),
                    genericParameters,
                    trees.StructTree.AttributeLists.SelectMany(x => x.Attributes)
                        .Where(a => a.ToString().StartsWith("Union"))
                        .SelectMany(a =>
                            a.ArgumentList?.Arguments.SelectMany(arg => Extensions.ParseUnvaluedStates(arg.ToString()))
                            ?? []).ToImmutableArray(),
                    trees.Usings ?? ImmutableArray<string>.Empty,
                    fields,
                    genericConstraints
                );
            })
            .ToImmutableArray();

        foreach (var unionDescriptor in types)
        {
            var code = UnionCodeGenerator.GenerateUnionPartialImplementation(unionDescriptor);

            var parsedCode = CSharpSyntaxTree.ParseText(code);
            var formattedCode = parsedCode.GetRoot().NormalizeWhitespace().ToFullString();

            context.AddSource($"{unionDescriptor.StructName}.Generated.cs", formattedCode);
        }
    }
}

file static class Extensions
{
    public static bool IsStruct(FieldDeclarationSyntax f, Compilation compilation)
    {
        var semantic = compilation.GetSemanticModel(f.SyntaxTree);
        var isValueType = semantic.GetDeclaredSymbol(f.Declaration.Variables.Last()) is IFieldSymbol
        {
            Type.IsValueType: true,
        };

        return isValueType;
    }

    public static string? GetFieldType(FieldDeclarationSyntax f, Compilation compilation)
    {
        var semantic = compilation.GetSemanticModel(f.SyntaxTree);
        var typeSemantic = semantic.GetDeclaredSymbol(f.Declaration.Variables.Last()) as IFieldSymbol;
        var fullType = typeSemantic?.Type.ToString() ?? string.Empty;

        var fullTypeSpan = fullType.ToCharArray().AsSpan();
        var typeSpan = fullTypeSpan.LastIndexOf('.') is var x && x > 0
            ? fullTypeSpan[(x + 1)..]
            : fullTypeSpan;

        typeSpan = typeSpan[^1] == '?' ? typeSpan[..^1] : typeSpan;
        var type = typeSpan.ToString();

        return type;
    }

    public static T? TraverseBack<T>(SyntaxNode syntaxTree) where T : SyntaxNode
    {
        var node = syntaxTree.Parent;
        while (node is not (null or T))
        {
            node = node?.Parent;
        }

        return node as T;
    }

    public static (string Argument, string Value) ParseArgumentSyntax(string syntax)
    {
        var span = syntax.AsSpan();
        var index = span.IndexOf('=');
        var hasSpaceBefore = span.IndexOf(' ') is { } x && x + 1 == index;
        var hasSpaceAfter = span.LastIndexOf(' ') is { } y && y - 1 == index;

        var argumentName = span[..(index - (hasSpaceBefore ? 1 : 0))];
        var argumentValue = span[(index + 1 + (hasSpaceAfter ? 1 : 0))..];

        return (argumentName.ToString(), argumentValue.ToString());
    }

    public static IEnumerable<string> ParseUnvaluedStates(string syntax) =>
        syntax.Split(",", StringSplitOptions.RemoveEmptyEntries);
}