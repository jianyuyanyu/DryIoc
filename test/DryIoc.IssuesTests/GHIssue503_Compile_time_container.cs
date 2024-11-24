using DryIoc.ImTools;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.Linq;
using UsingExample;
using DryIoc.FastExpressionCompiler.LightExpression;
using System.Text;

namespace DryIoc.IssuesTests;

[TestFixture]
public sealed class GHIssue503_Compile_time_container : ITest
{
    public int Run()
    {
        Generate_the_full_compile_time_container_code();
        return 1;
    }

    [Test]
    public void Generate_the_full_compile_time_container_code()
    {
        var c = new Container();

        var container = MyCompileTimeDI.RegisterInContainer().With(rules => rules.WithExpressionGenerationSettingsOnly());

        var result = container.GenerateResolutionExpressions(x => x.SelectMany(r =>
            MyCompileTimeDI.SpecifyResolutionRoots(r).EmptyIfNull()).Concat(MyCompileTimeDI.CustomResolutionRoots.EmptyIfNull()));

        string TrimUsings(string source)
        {
            source = source.Replace("DryIoc.", "");
            // todo: @wip remove unnecessary usings that's are System.Collections.Generic for KeyValuePair, etc.
            foreach (var x in MyCompileTimeDI.NamespaceUsings)
                source = source.Replace(x + ".", "");
            return source;
        }

        string Code(object x, int lineIndent = 0) =>
            x == null ? "null" :
            x is Expression e ? TrimUsings(e.ToCSharpString(new StringBuilder(), lineIndent).ToString()) :
            x is Request r ? Code(container.GetRequestExpression(r), lineIndent) :
            Code(container.GetConstantExpression(x, x.GetType(), true), lineIndent);

        // trim `typeof` and namespaces included in usings
        string TypeOnlyCode(Type type) => TrimUsings(type.ToCode(printGenericTypeArgs: true));

        string GetTypeNameOnly(string typeName) => typeName.Split('`').First().Split('.').Last();

        string CommaOptArg(string arg) => arg == "null" ? "" : ", " + arg;

        var methodsBodyLineIdent = 8;

        var rootCodes = result.Roots.Select((r, i) =>
            new
            {
                r.Key.ServiceType,
                ServiceTypeCode = Code(r.Key.ServiceType),
                ServiceTypeOnlyCode = TypeOnlyCode(r.Key.ServiceType),
                ServiceKeyCode = Code(r.Key.ServiceKey),
                RequiredServiceTypeCode = Code(r.Key.Details.RequiredServiceType),
                ExpressionCode = Code(r.Value.Body, methodsBodyLineIdent),
                CreateMethodName = "Get_" + GetTypeNameOnly(r.Key.ServiceType.Name) + "_" + i
            }).ToArray();

        var defaultRoots = rootCodes.Match(f => f.ServiceKeyCode == "null");

        var depCodes = result.ResolveDependencies.Select((r, i) =>
            new
            {
                r.Key.ServiceType,
                ServiceTypeCode = Code(r.Key.ServiceType),
                ServiceTypeOnlyCode = TypeOnlyCode(r.Key.ServiceType),
                ServiceKeyCode = Code(r.Key.ServiceKey),
                r.Key.ServiceKey,
                ExpressionCode = Code(r.Value, methodsBodyLineIdent),
                Expression = r.Value,
                RequiredServiceTypeCode = Code(r.Key.RequiredServiceType),
                PreResolveParentCode = Code(r.Key.Parent, methodsBodyLineIdent + 8),
                PreResolveParent = r.Key.Parent,
                CreateMethodName = "GetDependency_" + GetTypeNameOnly(r.Key.ServiceType.Name) + "_" + i
            }).ToArray();

        var includeVariants = container.Rules.VariantGenericTypesInResolvedCollection;

        var sb = new StringBuilder(4096);
        sb.Append(
            """
            // <auto-generated/>
            /*
            The MIT License (MIT)

            Copyright (c) 2016-2024 Maksim Volkau

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in
            all copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
            THE SOFTWARE.

            =================================================================================================
            The code below is generated at compile-time and changes here will be lost on the next generation.
            =================================================================================================
            """);

        var errCount = result.Errors.Count;
        if (errCount != 0)
        {
            sb.Append(
                $"""

                There are {errCount} generation ERRORS:
                
                
                """);

            var eNum = 0;
            foreach (var e in result.Errors)
                sb.Append(
                    $"""

                    {++eNum}. {e.Key}:
                    {e.Value.Message}


                    """);
        }

        var notResolvedDeps = 0;
        foreach (var dc in depCodes)
            if (dc.Expression == null)
            {
                if (notResolvedDeps++ == 0)
                    sb.Append(
                        """

                        WARNINGS: Some dependencies are missing. Register them at runtime or add to the compile-time registrations.


                        """);

                // todo: @wip remove unnecessary info from the output
                sb.Append(
                    $"""

                    `{dc.ServiceTypeOnlyCode}` {(dc.ServiceKey == null ? "" : $"with key {dc.ServiceKeyCode} ")}in {dc.PreResolveParent}
                    
                    """);
            }

        if (notResolvedDeps > 0)
            depCodes = depCodes.Match(static d => d.Expression != null);

        sb.Append(
            """
            --------------------------------------------------------------------------------------------------------
            */

            namespace DryIoc; // todo: @wip can we use User namespace here?

            using System;
            using System.Collections.Generic;
            using System.Threading;
            using DryIoc.ImTools;

            // Usings set in the `NamespaceUsings`:
            """);

        foreach (var ns in MyCompileTimeDI.NamespaceUsings)
            sb.Append(
                $"""

                using {ns};
                
                """);
        sb.Append(
            """

            // todo: @wip customize the container class name
            ///<summary>The container provides access to the object graph generated using the DryIoc own tools at compile-time</summary>
            public sealed class CompileTimeContainer : ICompileTimeContainer
            {
                ///<summary>The instance if generated compile-time container.</summary>
                public static readonly CompileTimeContainer Instance = new CompileTimeContainer();

                // todo: @wip tbd
                /// <inheritdoc/>
                public bool IsRegistered(Type serviceType) => false;
                /// <inheritdoc/>
                public bool IsRegistered(Type serviceType, object serviceKey) => false;

                /// <inheritdoc/>
                public bool TryResolve(out object service, IResolverContext r, Type serviceType)
                {
            """);
        for (var i = 0; i < defaultRoots.Length; ++i)
            sb.Append(
                $$"""

                        {{(i > 0 ? "else " : "")}}if (serviceType == {{defaultRoots[i].ServiceTypeCode}})
                        {
                            service = {{defaultRoots[i].CreateMethodName}}(r);
                            return true;
                        }
                """);
        sb.Append(
            """

                    service = null;
                    return false;
                }

            """);


        sb.Append(
            """

                /// <inheritdoc/>
                public bool TryResolve(out object service, IResolverContext r,
                    Type serviceType, object serviceKey, Type requiredServiceType, Request preRequestParent, object[] args)
                {
            """);

        var index = 0;
        foreach (var rootGroup in rootCodes.Where(x => x.ServiceKeyCode != "null").GroupBy(x => x.ServiceType))
        {
            sb.Append(
                $$"""

                        {{(index++ > 0 ? "else " : "")}}if (serviceType == {{rootGroup.Key}})
                        {
                """);
            var innerIndex = 0;
            foreach (var root in rootGroup)
            {
                sb.Append(
                    $$"""

                                {{(innerIndex++ > 0 ? "else " : "")}}if ({{root.ServiceKeyCode}}.Equals(serviceKey))
                                {
                                    service = {{root.CreateMethodName}}(r);
                                    return true;
                                }
                    """);
            }
            sb.Append(
                """

                        }
                """);
        }

        index = 0;
        foreach (var depGroup in depCodes.GroupBy(x => x.ServiceType))
        {
            sb.Append(
                $$"""

                        {{(index++ > 0 ? "else " : "")}}if (serviceType == {{depGroup.Key}})
                        {
                """);


            var innerIndex = 0;
            foreach (var dep in depGroup)
            {
                sb.Append(
                    $$"""

                                {{(innerIndex++ > 0 ? "else " : "")}}if ({{(dep.ServiceKey == null ? "serviceKey == null" :
                                        dep.ServiceKey is DefaultKey ? "(serviceKey == null || " + dep.ServiceKeyCode + ".Equals(serviceKey))" :
                                        dep.ServiceKeyCode + ".Equals(serviceKey)")}} &&
                                    requiredServiceType == {{dep.RequiredServiceTypeCode}} &&
                                    Equals(preRequestParent, {{dep.PreResolveParentCode}}))
                                {
                                    service = {{dep.CreateMethodName}}(r);
                                    return true;
                                }
                    """);
            }
            sb.Append(
                """

                        }
                """);
        }

        sb.Append(
            """

                    service = null;
                    return false;
                }

            """);

        sb.Append(
            """

                /// <inheritdoc/>
                public IEnumerable<ResolveManyResult> ResolveMany(IResolverContext _, Type serviceType)
            """);

        if (rootCodes.Length == 0)
            sb.Append(" => ArrayTools.Empty<ResolveManyResult>();");
        else
        {
            sb.Append(
                """

                    {
                """);
            foreach (var serviceTypeGroup in rootCodes.GroupBy(x => x.ServiceType))
            {
                sb.Append(
                    $$"""

                            if (serviceType == {{serviceTypeGroup.First().ServiceTypeCode}})
                            {
                    """);
                foreach (var reg in serviceTypeGroup)
                    sb.Append(
                        $"""

                                    yield return ResolveManyResult.Of(r => {reg.CreateMethodName}(r){CommaOptArg(reg.ServiceKeyCode)}{CommaOptArg(reg.RequiredServiceTypeCode)});
                        """);

                if (includeVariants && serviceTypeGroup.Key.IsGenericType)
                {
                    var sourceType = serviceTypeGroup.Key;
                    var variants = rootCodes
                        .Where(x => x.ServiceType.IsGenericType &&
                            x.ServiceType.GetGenericTypeDefinition() == sourceType.GetGenericTypeDefinition() &&
                            x.ServiceType != sourceType && x.ServiceType.IsAssignableTo(sourceType));
                    foreach (var variant in variants)
                    {
                        sb.Append(
                            $"""

                                        yield return ResolveManyResult.Of(r => {variant.CreateMethodName}(r){CommaOptArg(variant.ServiceKeyCode)}{CommaOptArg(variant.RequiredServiceTypeCode)});
                            """);
                    }
                }

                sb.Append(
                    """

                            }
                    """);
            }
            sb.Append(
                """

                    }

                """);
        }

        sb.Append(
            """

                // Resolution roots supposed to be Resolved by the clients:

            """);

        foreach (var root in rootCodes)
        {
            sb.Append(
                $"""

                    internal static {root.ServiceTypeOnlyCode} {root.CreateMethodName}(IResolverContext r) =>
                        {root.ExpressionCode};

                """);
        }

        sb.Append(
            """

                // Dependencies injected through the Resolve call, e.g. Bar in `r => new Foo(r.Resolve<Bar>())`:

            """);

        foreach (var dep in depCodes)
        {
            sb.Append(
                $"""

                    internal static {dep.ServiceTypeOnlyCode} {dep.CreateMethodName}(IResolverContext r) =>
                        {dep.ExpressionCode};

                """);
        }

        sb.Append(
            """
            }

            """);

        var @cs = sb.ToString();
        StringAssert.Contains("serviceType == ", @cs);
    }
}
