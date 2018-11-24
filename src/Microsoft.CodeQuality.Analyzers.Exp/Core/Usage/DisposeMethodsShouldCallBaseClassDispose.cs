﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeQuality.Analyzers.Exp.Usage
{
    /// <summary>
    /// CA2215: Dispose methods should call base class dispose
    /// 
    /// A type that implements System.IDisposable inherits from a type that also implements IDisposable.
    /// The Dispose method of the inheriting type does not call the Dispose method of the parent type.
    /// To fix a violation of this rule, call base.Dispose in your Dispose method.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public sealed class DisposeMethodsShouldCallBaseClassDispose : DiagnosticAnalyzer
    {
        internal const string RuleId = "CA2215";

        private static readonly LocalizableString s_localizableTitle = new LocalizableResourceString(nameof(MicrosoftUsageAnalyzersResources.DisposeMethodsShouldCallBaseClassDisposeTitle), MicrosoftUsageAnalyzersResources.ResourceManager, typeof(MicrosoftUsageAnalyzersResources));
        private static readonly LocalizableString s_localizableMessage = new LocalizableResourceString(nameof(MicrosoftUsageAnalyzersResources.DisposeMethodsShouldCallBaseClassDisposeMessage), MicrosoftUsageAnalyzersResources.ResourceManager, typeof(MicrosoftUsageAnalyzersResources));
        private static readonly LocalizableString s_localizableDescription = new LocalizableResourceString(nameof(MicrosoftUsageAnalyzersResources.DisposeMethodsShouldCallBaseClassDisposeDescription), MicrosoftUsageAnalyzersResources.ResourceManager, typeof(MicrosoftUsageAnalyzersResources));

        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId,
                                                                             s_localizableTitle,
                                                                             s_localizableMessage,
                                                                             DiagnosticCategory.Usage,
                                                                             DiagnosticHelpers.DefaultDiagnosticSeverity,
                                                                             isEnabledByDefault: DiagnosticHelpers.EnabledByDefaultIfNotBuildingVSIX,
                                                                             description: s_localizableDescription,
                                                                             helpLinkUri: "https://docs.microsoft.com/visualstudio/code-quality/ca2213-disposable-fields-should-be-disposed",
                                                                             customTags: FxCopWellKnownDiagnosticTags.PortedFxCopRule);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(compilationContext =>
            {
                if (!DisposeAnalysisHelper.TryGetOrCreate(compilationContext.Compilation, out DisposeAnalysisHelper disposeAnalysisHelper))
                {
                    return;
                }

                compilationContext.RegisterOperationBlockStartAction(operationBlockStartContext =>
                {
                    if (!(operationBlockStartContext.OwningSymbol is IMethodSymbol containingMethod) ||
                        containingMethod.OverriddenMethod == null ||
                        containingMethod.OverriddenMethod.IsAbstract)
                    {
                        return;
                    }

                    var disposeMethodKind = containingMethod.GetDisposeMethodKind(disposeAnalysisHelper.IDisposable);
                    switch (disposeMethodKind)
                    {
                        case DisposeMethodKind.Dispose:
                        case DisposeMethodKind.DisposeBool:
                            break;

                        case DisposeMethodKind.Close:
                            // FxCop compat: Ignore Close methods due to high false positive rate.
                            return;

                        default:
                            return;
                    }

                    var invokesBaseDispose = false;
                    operationBlockStartContext.RegisterOperationAction(operationContext =>
                    {
                        if (invokesBaseDispose)
                        {
                            return;
                        }

                        var invocation = (IInvocationOperation)operationContext.Operation;
                        if (invocation.TargetMethod == containingMethod.OverriddenMethod &&
                            invocation.Instance.GetInstanceReferenceKind() == InstanceReferenceKind.Base)
                        {
                            Debug.Assert(invocation.TargetMethod.GetDisposeMethodKind(disposeAnalysisHelper.IDisposable) == disposeMethodKind);
                            invokesBaseDispose = true;
                        }
                    }, OperationKind.Invocation);

                    operationBlockStartContext.RegisterOperationBlockEndAction(operationEndContext =>
                    {
                        if (!invokesBaseDispose)
                        {
                            // Ensure that method '{0}' calls '{1}' in all possible control flow paths.
                            var arg1 = containingMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                            var baseKeyword = containingMethod.Language == LanguageNames.CSharp ? "base" : "MyBase";
                            var disposeMethodParam = disposeMethodKind == DisposeMethodKind.Dispose ?
                                string.Empty :
                                containingMethod.Language == LanguageNames.CSharp ? "bool" : "Boolean";
                            var arg2 = $"{baseKeyword}.Dispose({disposeMethodParam})";
                            var diagnostic = containingMethod.CreateDiagnostic(Rule, arg1, arg2);
                            operationEndContext.ReportDiagnostic(diagnostic);
                        }
                    });
                });
            });
        }
    }
}
