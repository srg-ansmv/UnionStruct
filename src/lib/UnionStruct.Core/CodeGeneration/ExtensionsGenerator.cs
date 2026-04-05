using System;
using System.Linq;
using System.Text;
using UnionStruct.Unions;

namespace UnionStruct.CodeGeneration;

public static class ExtensionsGenerator
{
    public static string Generate(UnionContext unionContext)
    {
        var rnd = new Random();
        var mangle = new string(Enumerable.Range(0, 12).Select(_ => (char)('A' + rnd.Next(26))).ToArray());

        return $$"""
                 {{GeneratedCodeAttributeLine.Code}}
                 public static class {{unionContext.Descriptor.StructName}}GeneratedExtensions{{mangle}}
                 {
                    {{GenerateAsyncMapExtensions(unionContext)}}
                    
                    {{GenerateAsyncFoldExtensions(unionContext)}}
                    
                    {{GenerateAsyncWhenExtensions(unionContext)}}
                 }
                 """;
    }

    private static string GenerateAsyncWhenExtensions(UnionContext context)
    {
        var allConstraints = string.Join('\n', context.Descriptor.GenericConstraints.Values);

        var unvaluedWhenMethods = context.UnvaluedEnums.Select(state =>
            $$"""
                public static Task<{{context.FullUnionDeclaration}}> When{{state}}{{context.GenericDeclaration}}(this Task<{{context.FullUnionDeclaration}}> src, Action body) 
                {{allConstraints}}
                {
                    return src.ContinueWith(
                        t => t switch 
                        {
                            { Exception: null } => t.Result.When{{state}}(body),
                            { Exception: not null } => throw new InvalidOperationException("Error when observing state {{state}}", innerException: t.Exception),
                            _ => throw new InvalidOperationException("Error when observing state {{state}}")
                        }
                    );
                }
                
                public static Task<{{context.FullUnionDeclaration}}> When{{state}}Async{{context.GenericDeclaration}}(this Task<{{context.FullUnionDeclaration}}> src, Func<Task> body) 
                {{allConstraints}}
                {
                    return src.ContinueWith(
                        t => t switch 
                        {
                            { Exception: null } => t.Result.When{{state}}Async(body),
                            { Exception: not null } => Task.FromException<{{context.FullUnionDeclaration}}>(new InvalidOperationException("Error when observing state {{state}}", innerException: t.Exception)),
                            _ => Task.FromException<{{context.FullUnionDeclaration}}>(new InvalidOperationException("Error when observing state {{state}}"))
                        }
                    ).Unwrap();
                }
              """
        );

        var stateWhenMethods = context.Descriptor.Fields.Select(descriptor =>
        {
            var state = context.FieldNameToEnumMap[descriptor.Name];

            return $$"""
                       public static Task<{{context.FullUnionDeclaration}}> When{{state}}{{context.GenericDeclaration}}(this Task<{{context.FullUnionDeclaration}}> src, Action<{{descriptor.Type}}> body) 
                       {{allConstraints}}
                       {
                           return src.ContinueWith(
                               t => t switch 
                               {
                                   { Exception: null } => t.Result.When{{state}}(body),
                                   { Exception: not null } => throw new InvalidOperationException("Error when observing state {{state}}", innerException: t.Exception),
                                   _ => throw new InvalidOperationException("Error when observing state {{state}}")
                               }
                           );
                       }
                       

                     public static Task<{{context.FullUnionDeclaration}}> When{{state}}Async{{context.GenericDeclaration}}(this Task<{{context.FullUnionDeclaration}}> src, Func<{{descriptor.Type}}, Task> body) 
                     {{allConstraints}}
                     {
                        return src.ContinueWith(
                             t => t switch 
                             {
                                { Exception: null } => t.Result.When{{state}}Async(body),
                                { Exception: not null } => Task.FromException<{{context.FullUnionDeclaration}}>(new InvalidOperationException("Error when observing state {{state}}", innerException: t.Exception)),
                                _ => Task.FromException<{{context.FullUnionDeclaration}}>(new InvalidOperationException("Error when observing state {{state}}"))
                             }
                        ).Unwrap();
                     }
                     """;
        });

        return string.Join("\n\n", unvaluedWhenMethods.Concat(stateWhenMethods));
    }

    private static string GenerateAsyncFoldExtensions(UnionContext unionContext)
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

        var parameters = string.Join(',', descriptors.Select(x => stateEnumMap[x.Name]).Concat(unionContext.UnvaluedEnums));

        var allConstraints = string.Join('\n', unionContext.Descriptor.GenericConstraints.Values);

        var initialGenerics = string.Join(",", unionContext.Descriptor.GenericParameters);
        initialGenerics = initialGenerics == string.Empty ? string.Empty : $"{initialGenerics}, ";

        return $$"""
                 public static Task<TOut> Fold<{{initialGenerics}}TOut>(this Task<{{unionContext.FullUnionDeclaration}}> src, {{funcParameters}})
                    {{allConstraints}}
                    { 
                        return src.ContinueWith(
                            t => t switch 
                            {
                                { Exception: null } => t.Result.Fold({{parameters}}),
                                { Exception: not null } => throw new InvalidOperationException("Error when folding", innerException: t.Exception),
                                _ => throw new InvalidOperationException("Error when folding") 
                            }
                        );
                    }


                 public static Task<TOut> FoldAsync<{{initialGenerics}}TOut>(this Task<{{unionContext.FullUnionDeclaration}}> src, {{funcAsyncParameters}})
                    {{allConstraints}}
                    { 
                        return src.ContinueWith(
                            t => t switch 
                            {
                                { Exception: null } => t.Result.FoldAsync({{parameters}}),
                                { Exception: not null } => Task.FromException<TOut>(new InvalidOperationException("Error when folding", innerException: t.Exception)),
                                _ => Task.FromException<TOut>(new InvalidOperationException("Error when folding"))
                            }
                        ).Unwrap();
                    }
                 """;
    }

    private static string GenerateAsyncMapExtensions(UnionContext context)
    {
        var descriptors = context.Descriptor.Fields;
        var stateEnumMap = context.FieldNameToEnumMap;
        var structName = context.Descriptor.StructName;
        var genericParams = context.Descriptor.GenericParameters;
        var fullStructName = context.FullUnionDeclaration;
        var initialGenerics = string.Join(",", context.Descriptor.GenericParameters);
        initialGenerics = initialGenerics == string.Empty ? string.Empty : initialGenerics;

        var allConstraints = string.Join('\n', context.Descriptor.GenericConstraints.Values);

        var sb = new StringBuilder();

        var query = descriptors
            .Where(x => x.UnionArguments.TryGetValue(nameof(UnionPartAttribute.AddMap), out var value) && value == "true")
            .Select((x, i) => (x, i, genericParams.Contains(x.Type)));

        foreach (var (descriptor, index, isGeneric) in query)
        {
            var stateName = stateEnumMap[descriptor.Name];

            var genericPlaceHolders = string.Join(",", Enumerable.Range(0, genericParams.Length).Select(x => $"{{{x}}}"));
            var outcomeParams = string.Format(genericPlaceHolders,
                genericParams.Select<string, object>((x, i) => isGeneric && i == index ? "TOut" : x).ToArray());
            var genericFormat = $"<{outcomeParams}>";

            var genericConstraint =
                isGeneric && context.Descriptor.GenericConstraints.TryGetValue(descriptor.Type, out var constraint)
                    ? constraint.Replace(descriptor.Type, "TOut")
                    : string.Empty;

            var methodConstraints = allConstraints + '\n' + genericConstraint;

            var newStructType = $"{structName}{(genericFormat == "<>" ? string.Empty : genericFormat)}";
            var funcType = isGeneric ? "TOut" : descriptor.Type;
            var genericType = isGeneric ? "TOut" : string.Empty;

            var parameters = isGeneric ? $"<{initialGenerics}, {genericType}>" : $"<{initialGenerics}>";
            parameters = parameters == "<>" ? string.Empty : parameters;

            sb.AppendLine(
                $$"""
                  public static Task<{{newStructType}}> Map{{stateName}}{{parameters}}(this Task<{{fullStructName}}> src, Func<{{descriptor.Type}}, {{funcType}}> mapper)
                        {{methodConstraints}}
                  {
                        return src.ContinueWith(
                            t => t switch 
                            {
                                { Exception: null } => t.Result.Map{{stateName}}(mapper),
                                { Exception: not null } => throw new InvalidOperationException("Error when mapping", innerException: t.Exception),
                                _ => throw new InvalidOperationException("Error when mapping") 
                            }
                        );
                  }
                  """
            );

            sb.AppendLine(
                $$"""
                  public static Task<{{newStructType}}> Map{{stateName}}{{parameters}}(this Task<{{fullStructName}}> src, Func<{{descriptor.Type}}, {{newStructType}}> mapper)
                        {{methodConstraints}}
                  {
                        return src.ContinueWith(
                            t => t switch 
                            {
                                { Exception: null } => t.Result.Map{{stateName}}(mapper),
                                { Exception: not null } => throw new InvalidOperationException("Error when mapping", innerException: t.Exception),
                                _ => throw new InvalidOperationException("Error when mapping") 
                            }
                        );
                  }
                  """
            );

            sb.AppendLine(
                $$"""
                  public static Task<{{newStructType}}> Map{{stateName}}Async{{parameters}}(this Task<{{fullStructName}}> src, Func<{{descriptor.Type}}, Task<{{funcType}}>> mapper)
                        {{methodConstraints}}
                  {
                        return src.ContinueWith(
                            t => t switch 
                            {
                                { Exception: null } => t.Result.Map{{stateName}}Async(mapper),
                                { Exception: not null } => Task.FromException<{{newStructType}}>(new InvalidOperationException("Error when mapping", innerException: t.Exception)),
                                _ => Task.FromException<{{newStructType}}>(new InvalidOperationException("Error when mapping"))
                            }
                        ).Unwrap();
                  }
                  """
            );

            sb.AppendLine(
                $$"""
                  public static Task<{{newStructType}}> Map{{stateName}}Async{{parameters}}(this Task<{{fullStructName}}> src, Func<{{descriptor.Type}}, Task<{{newStructType}}>> mapper)
                        {{methodConstraints}}
                  {
                        return src.ContinueWith(
                            t => t switch 
                            {
                                { Exception: null } => t.Result.Map{{stateName}}Async(mapper),
                                { Exception: not null } => Task.FromException<{{newStructType}}>(new InvalidOperationException("Error when mapping", innerException: t.Exception)),
                                _ => Task.FromException<{{newStructType}}>(new InvalidOperationException("Error when mapping"))
                            }
                        ).Unwrap();
                  }
                  """
            );
        }

        return sb.ToString();
    }
}