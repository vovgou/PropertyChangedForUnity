﻿using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#pragma warning disable CS0067
static class SourceGeneratorEngine
{
    const string sourceFileHintName = "PropertyChanged.g.cs";

    public static void GenerateSource(SourceProductionContext context, Compilation compilation, Configuration configuration, ImmutableArray<ClassDeclarationSyntax> classes)
    {
        var cancellationToken = context.CancellationToken;

        if (configuration.IsDisabled)
        {
            context.AddSource(sourceFileHintName, @"// Source generator is disabled by configuration.");
            return;
        }

        var eventInvokerName = configuration.EventInvokerName.NullIfEmpty() ?? "OnPropertyChanged";

        var codeBuilder = new CodeBuilder();

        try
        {
            codeBuilder
                .Add("// <auto-generated/>")
                .Add("#nullable enable")
                .Add("#pragma warning disable CS0067")
                .Add("using System.ComponentModel;")
                .Add("using System.Runtime.CompilerServices;");

            var classesByNamespace = classes.GroupBy(item => item.SyntaxTree)
                .Select(group => new { SemanticModel = compilation.GetSemanticModel(group.Key), Items = group })
                .SelectMany(group => group.Items.Select(classDeclaration => new { TypeSymbol = group.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) as ITypeSymbol, ClassDeclaration = classDeclaration }))
                .GroupBy(item => item.TypeSymbol?.ContainingNamespace?.Name);

            foreach (var group in classesByNamespace)
                using (codeBuilder.AddBlock("namespace {0}", group.Key))
                {
                    foreach (var item in group)
                    {
                        var typeSymbol = item.TypeSymbol;
                        if (typeSymbol == null)
                            continue;

                        var classDeclaration = item.ClassDeclaration;

                        var isSealed = classDeclaration.Modifiers.Any(token => token.IsKind(SyntaxKind.SealedKeyword));
                        var hasBase = classDeclaration.BaseList.GetInterfaceTypeCandidates().Any();

                        var baseDefinition = hasBase ? string.Empty : " : INotifyPropertyChanged";

                        using (codeBuilder.AddBlock($"{classDeclaration.Modifiers} class {typeSymbol.Name}{baseDefinition}"))
                        {
                            codeBuilder.Add("public event PropertyChangedEventHandler? PropertyChanged;");

                            var modifiers1 = isSealed ? "private" : "protected";

                            using (codeBuilder.AddBlock($"{modifiers1} void {eventInvokerName}([CallerMemberName] string? propertyName = null)"))
                            {
                                codeBuilder.Add("OnPropertyChanged(new PropertyChangedEventArgs(propertyName));");
                            }

                            var modifiers2 = isSealed ? "private" : "protected virtual";

                            using (codeBuilder.AddBlock($"{modifiers2} void {eventInvokerName}(PropertyChangedEventArgs eventArgs)"))
                            {
                                codeBuilder.Add("PropertyChanged?.Invoke(this, eventArgs);");
                            }
                        }
                    }
                }
        }
        catch (Exception ex)
        {
            codeBuilder.Add("/*");
            codeBuilder.Add($"GenerateSource failed: {ex}");
            codeBuilder.Add("*/");
        }

        context.AddSource(sourceFileHintName, codeBuilder.ToString());
    }
}