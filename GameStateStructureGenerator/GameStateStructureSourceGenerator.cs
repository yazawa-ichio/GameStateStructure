using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameStateStructure.Generator
{

	[Generator]
	public partial class GameStateStructureSourceGenerator : ISourceGenerator
	{
		HashSet<string> m_Reported;

		bool m_StrictEvent;

		public void Execute(GeneratorExecutionContext context)
		{
			try
			{
				Generate(context);
			}
			catch (Exception err)
			{
				context.ReportDiagnostic(Diagnostic.Create(Diagnostics.GeneratorFailure, Location.None, err.ToString()));
			}
		}

		private void Generate(GeneratorExecutionContext context)
		{
			string moduleName = context.Compilation.SourceModule.Name;
			if (moduleName.StartsWith("UnityEngine."))
			{
				return;
			}

			if (moduleName.StartsWith("UnityEditor."))
			{
				return;
			}

			if (moduleName.StartsWith("Unity."))
			{
				return;
			}

			SyntaxReceiver receiver = context.SyntaxReceiver as SyntaxReceiver;
			m_Reported = new HashSet<string>();
			m_StrictEvent = context.ParseOptions is CSharpParseOptions parseOptions
				&& parseOptions.PreprocessorSymbolNames.Contains("GSS_STRICT_EVENT");

			foreach (var node in receiver.SubscribeEventMethods)
			{
				SemanticModel model = context.Compilation.GetSemanticModel(node.SyntaxTree);
				if (model.GetDeclaredSymbol(node, context.CancellationToken) is IMethodSymbol method)
				{
					CheckSubscribeEvent(context, method);
				}
			}

			foreach (var node in receiver.List)
			{
				SemanticModel semanticModel = context.Compilation.GetSemanticModel(node.SyntaxTree);
				ISymbol symbol = semanticModel.GetDeclaredSymbol(node, context.CancellationToken);

				if (!CheckPartial(context, node, symbol))
				{
					continue;
				}

				var signatures = new HashSet<string>();

				foreach (var attr in symbol.GetAttributes())
				{
					if (TransAttribute.TryGet(attr, out TransAttribute data))
					{
						if (!CheckTransTarget(context, symbol, attr, data))
						{
							continue;
						}
						if (!CheckSignature(context, signatures, attr, GetTransSignature(data)))
						{
							continue;
						}
						GenerateTransMethod(context, symbol, data);
					}
					if (PublishEventData.TryGet(attr, out PublishEventData publishEventData))
					{
						CheckSubscribers(context, publishEventData);
						if (publishEventData.Events.Count == 0)
						{
							if (!publishEventData.HasInternalOnly)
							{
								Report(context, Diagnostics.PublishEventHasNoSubscriber, GetLocation(attr), publishEventData.Type.ToDisplayString());
							}
							continue;
						}
						var duplicated = false;
						foreach (var e in publishEventData.Events)
						{
							if (!CheckSignature(context, signatures, attr, GetEventSignature(publishEventData, e)))
							{
								duplicated = true;
							}
						}
						if (duplicated)
						{
							continue;
						}
						GeneratePublishEventMethod(context, symbol, publishEventData);
					}
				}
			}
		}

		static Location GetLocation(AttributeData attr)
		{
			return attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;
		}

		void Report(GeneratorExecutionContext context, DiagnosticDescriptor descriptor, Location location, params object[] args)
		{
			var key = descriptor.Id + "|" + location.GetLineSpan() + "|" + string.Join(",", args);
			if (!m_Reported.Add(key))
			{
				return;
			}
			context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
		}

		bool CheckPartial(GeneratorExecutionContext context, TypeDeclarationSyntax node, ISymbol symbol)
		{
			var ok = true;
			foreach (var declaration in node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>())
			{
				if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
				{
					continue;
				}
				Report(context, Diagnostics.MustBePartial, declaration.Identifier.GetLocation(), declaration.Identifier.Text);
				ok = false;
			}
			return ok;
		}

		bool CheckTransTarget(GeneratorExecutionContext context, ISymbol declaring, AttributeData attr, TransAttribute data)
		{
			var location = GetLocation(attr);
			var target = data.Symbol;
			if (target == null)
			{
				return false;
			}

			var ok = true;

			if (!IsGameState(target))
			{
				Report(context, Diagnostics.TargetMustBeGameState, location, target.ToDisplayString());
				ok = false;
			}

			var reason = GetNotConstructibleReason(target);
			if (reason != null)
			{
				Report(context, Diagnostics.TargetMustBeConstructible, location, target.ToDisplayString(), reason);
				ok = false;
			}

			if (!string.IsNullOrEmpty(data.Name) && !SyntaxFacts.IsValidIdentifier(data.Name))
			{
				Report(context, Diagnostics.InvalidName, location, data.Name);
				ok = false;
			}

			var isProcess = StateData.IsProcessType(target);

			if (data.Type == TransAttribute.ChangeType.GoTo && isProcess)
			{
				Report(context, Diagnostics.GoToTargetMustNotBeProcess, location, target.ToDisplayString());
				ok = false;
			}

			if (data.Type == TransAttribute.ChangeType.GoTo
				&& declaring is ITypeSymbol declaringType
				&& StateData.IsProcessType(declaringType))
			{
				Report(context, Diagnostics.ProcessCanNotDeclareGoTo, location, declaringType.ToDisplayString());
				ok = false;
			}

			if (data.Branch != null)
			{
				if (!isProcess)
				{
					Report(context, Diagnostics.BranchTargetMustBeProcess, location, target.ToDisplayString());
					ok = false;
				}
				var branchReason = GetInvalidBranchReason(data.Branch);
				if (branchReason != null)
				{
					Report(context, Diagnostics.InvalidBranch, location, data.Branch.ToDisplayString(), branchReason);
					ok = false;
				}
			}

			// 引数の収集と同じ範囲を見る（StateData.Create も基底を辿る）
			for (INamedTypeSymbol type = target as INamedTypeSymbol; type != null; type = type.BaseType)
			{
				foreach (var member in type.GetMembers())
				{
					if (member is not IPropertySymbol property)
					{
						continue;
					}
					if (!property.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == "GameStateStructure.ArgAttribute"))
					{
						continue;
					}
					if (property.SetMethod != null)
					{
						continue;
					}
					var propertyLocation = property.Locations.FirstOrDefault() ?? location;
					Report(context, Diagnostics.ArgMustHaveSetter, propertyLocation, property.Name);
					ok = false;
				}
			}

			return ok;
		}

		bool CheckSignature(GeneratorExecutionContext context, HashSet<string> signatures, AttributeData attr, string signature)
		{
			if (signatures.Add(signature))
			{
				return true;
			}
			Report(context, Diagnostics.DuplicateGeneratedMethod, GetLocation(attr), signature);
			return false;
		}

		static string GetTransSignature(TransAttribute attr)
		{
			var stateData = StateData.Create(attr.Symbol);
			var methodName = attr.Name ?? stateData.ShortName;
			string prefix;
			if (stateData.IsProcess)
			{
				prefix = "Run";
			}
			else if (attr.Type == TransAttribute.ChangeType.GoTo)
			{
				prefix = "GoTo";
			}
			else
			{
				prefix = "Push";
			}
			var args = stateData.Args.Select(x => x.Option ? $"Option<{x.Type}>" : x.Type).ToList();
			if (stateData.IsProcess)
			{
				args.Add("System.Threading.CancellationToken");
			}
			var arity = attr.Symbol is INamedTypeSymbol named && named.IsSealed ? "" : "<>";
			return $"{prefix}{methodName}{arity}({string.Join(", ", args)})";
		}

		static string GetEventSignature(PublishEventData data, EventMethod method)
		{
			var args = method.Symbol.Parameters.Select(x => x.Type.ToDisplayString());
			return $"{data.Prefix}{method.Symbol.Name}({string.Join(", ", args)})";
		}

		void CheckSubscribers(GeneratorExecutionContext context, PublishEventData data)
		{
			foreach (var member in data.Type.GetMembers())
			{
				if (member is not IMethodSymbol method)
				{
					continue;
				}
				if (!method.GetAttributes().Any(x => SubscribeEventAttribute.TryGet(x, out _))
					&& !data.Type.GetAttributes().Any(x => SubscribeEventAttribute.TryGet(x, out _)))
				{
					continue;
				}
				CheckSubscribeEvent(context, method);
			}
		}

		void CheckSubscribeEvent(GeneratorExecutionContext context, IMethodSymbol method)
		{
			var location = method.Locations.FirstOrDefault() ?? Location.None;

			if (method.IsStatic)
			{
				Report(context, Diagnostics.SubscribeEventMustBeInstanceMethod, location, method.Name);
			}

			if (method.IsGenericMethod)
			{
				Report(context, Diagnostics.SubscribeEventMustNotBeGeneric, location, method.Name);
			}

			if (method.DeclaredAccessibility != Accessibility.Public)
			{
				Report(context, Diagnostics.SubscribeEventMustBePublic, location, method.Name);
				return;
			}

			if (!IsSupportedEventReturnType(method.ReturnType))
			{
				Report(context, Diagnostics.SubscribeEventReturnTypeIsDiscarded, location, method.Name, method.ReturnType.ToDisplayString());
			}

			if (!m_StrictEvent)
			{
				return;
			}
			foreach (var parameter in method.Parameters)
			{
				if (IsEditableEventParameter(parameter))
				{
					continue;
				}
				Report(context, Diagnostics.SubscribeEventParameterIsNotEditable, location,
					method.Name, parameter.Name, parameter.Type.ToDisplayString());
			}
		}

		static bool IsEditableEventParameter(IParameterSymbol parameter)
		{
			if (parameter.RefKind != RefKind.None)
			{
				return false;
			}
			if (parameter.Type.TypeKind == TypeKind.Enum)
			{
				return true;
			}
			switch (parameter.Type.SpecialType)
			{
				case SpecialType.System_Boolean:
				case SpecialType.System_Int32:
				case SpecialType.System_Single:
				case SpecialType.System_String:
					return true;
			}
			return false;
		}

		static bool IsSupportedEventReturnType(ITypeSymbol type)
		{
			switch (type.ToDisplayString())
			{
				case "void":
				case "System.Threading.Tasks.Task":
				case "System.Threading.Tasks.ValueTask":
				case "Cysharp.Threading.Tasks.UniTask":
					return true;
			}
			return false;
		}

		static bool IsGameState(ITypeSymbol symbol)
		{
			for (ITypeSymbol type = symbol; type != null; type = type.BaseType)
			{
				if (type.ToDisplayString() == "GameStateStructure.GameState")
				{
					return true;
				}
			}
			return false;
		}

		static void EmitConstructorPreserve(
			CodeEmitter emitter, ITypeSymbol target, Compilation compilation, string methodSuffix)
		{
			if (target is not INamedTypeSymbol named)
			{
				return;
			}
			if (!IsReferenced(named.ContainingAssembly, compilation))
			{
				return;
			}
			var from = compilation.Assembly;
			var targets = named.InstanceConstructors
				.Where(x => compilation.IsSymbolAccessibleWithin(x, from))
				.Where(x => x.Parameters.All(p => p.RefKind == RefKind.None))
				.Where(x => x.Parameters.All(p => CanWriteDefault(p.Type, compilation)))
				.ToArray();
			if (targets.Length == 0)
			{
				return;
			}

			emitter.AppendLine("#if !GSS_DISABLE_CONSTRUCTOR_PRESERVE");
			emitter.AppendLine("// 管理コード除去でコンストラクタが消えないようにするための参照。実行されない。");
			emitter.AppendLine("[UnityEngine.Scripting.Preserve]");
			emitter.AppendLine($"static void PreserveConstructor{methodSuffix}()");
			using (emitter.Brace())
			{
				foreach (var ctor in targets)
				{
					var parameters = string.Join(", ", ctor.Parameters
						.Select(x => $"default({x.Type.ToDisplayString()})"));
					emitter.AppendLine($"_ = new {target.ToDisplayString()}({parameters});");
				}
			}
			emitter.AppendLine("#endif");
			emitter.NewLine();
		}

		static bool CanWriteDefault(ITypeSymbol type, Compilation compilation)
		{
			switch (type)
			{
				case IArrayTypeSymbol array:
					return CanWriteDefault(array.ElementType, compilation);
				case ITypeParameterSymbol:
				case IDynamicTypeSymbol:
					return true;
				case INamedTypeSymbol named:
					if (!IsReferenced(named.ContainingAssembly, compilation))
					{
						return false;
					}
					if (!compilation.IsSymbolAccessibleWithin(named, compilation.Assembly))
					{
						return false;
					}
					foreach (var argument in named.TypeArguments)
					{
						if (!CanWriteDefault(argument, compilation))
						{
							return false;
						}
					}
					return true;
				default:
					return false;
			}
		}

		static bool IsReferenced(IAssemblySymbol assembly, Compilation compilation)
		{
			if (assembly == null)
			{
				return true;
			}
			if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
			{
				return true;
			}
			foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
			{
				if (SymbolEqualityComparer.Default.Equals(assembly, reference))
				{
					return true;
				}
			}
			return false;
		}

		// 生成コードは利用側が付けた名前をそのまま書く。
		// キーワードと同じ名前でも通るようにする。
		static string Escape(string name)
		{
			return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
		}

		// 生成コードが自前で使う識別子。利用側の名前と重なったらずらす。
		// @ を付けても解決しない（@parameter と parameter は同じ識別子）。
		static string Unique(string candidate, ICollection<string> taken)
		{
			while (taken.Contains(candidate))
			{
				candidate += "_";
			}
			return candidate;
		}

		static string Identifier(string name)
		{
			var builder = new System.Text.StringBuilder(name.Length);
			foreach (var c in name)
			{
				builder.Append(char.IsLetterOrDigit(c) ? c : '_');
			}
			return builder.ToString();
		}

		static string GetInvalidBranchReason(ITypeSymbol symbol)
		{
			var derived = false;
			for (ITypeSymbol type = symbol; type != null; type = type.BaseType)
			{
				if (type.ToDisplayString() == "GameStateStructure.Branch")
				{
					derived = true;
					break;
				}
			}
			if (!derived)
			{
				return "GameStateStructure.Branch を継承していない";
			}
			if (symbol.IsAbstract)
			{
				return "abstract である";
			}
			if (symbol is INamedTypeSymbol named)
			{
				foreach (var ctor in named.InstanceConstructors)
				{
					if (ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public)
					{
						return null;
					}
				}
				return "public な引数なしコンストラクタが無い";
			}
			return null;
		}

		static string GetNotConstructibleReason(ITypeSymbol symbol)
		{
			if (symbol.IsAbstract)
			{
				return "abstract である";
			}
			return null;
		}

		public void Initialize(GeneratorInitializationContext context)
		{
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}

		private void GeneratePublishEventMethod(GeneratorExecutionContext context, ISymbol symbol, PublishEventData attr)
		{
			string fileName = $"{symbol.Name}.Event.{attr.Type.ToDisplayString()}.cs";

			CodeEmitter emitter = new();
			emitter.AppendLine("// <auto-generated />");
			emitter.AppendLine("using GameStateStructure;");
			emitter.AppendLine("using System.Threading;");
			emitter.AppendLine("using System.Threading.Tasks;");
			emitter.NewLine();

			Stack<IDisposable> brace = new();

			if (!symbol.ContainingNamespace.IsGlobalNamespace)
			{
				emitter.AppendLine($"namespace {symbol.ContainingNamespace.ToDisplayString()}");
				brace.Push(emitter.Brace());
			}

			Stack<INamedTypeSymbol> parent = new();
			INamedTypeSymbol nestType = symbol.ContainingType;
			while (nestType != null)
			{
				parent.Push(nestType);
				nestType = nestType.ContainingType;
			}
			while (parent.Count > 0)
			{
				INamedTypeSymbol p = parent.Pop();
				emitter.AppendLine($"partial class {p.Name}");
				brace.Push(emitter.Brace());
			}

			emitter.AppendLine($"partial class {symbol.Name}");

			using (emitter.Brace())
			{
				foreach (var e in attr.Events)
				{
					var argsStrings = e.Symbol.Parameters.Select(x => GetArg(x));

					string argParam = string.Join(", ", argsStrings);
					string target = Unique("state", new HashSet<string>(e.Symbol.Parameters.Select(x => x.Name)));

					if (e.IsAsync())
					{
						emitter.AppendLine($"protected async {e.Symbol.ReturnType.ToDisplayString()} {attr.Prefix}{e.Symbol.Name}({argParam})");
					}
					else
					{
						emitter.AppendLine($"protected void {attr.Prefix}{e.Symbol.Name}({argParam})");
					}

					using (emitter.Brace())
					{

						emitter.AppendLine($"if (!Active) return;");

						var inputStrings = e.Symbol.Parameters.Select(x => Escape(x.Name));

						string inputParam = string.Join(", ", inputStrings);

						if (e.Subscribe.ChildOnly && !e.Subscribe.Broadcast)
						{
							emitter.AppendLine($"var {target} = FindParentState<{attr.Type.ToDisplayString()}>();");
							emitter.AppendLine($"if ({target} == null) return;");
							if (e.IsAsync())
							{
								emitter.AppendLine($"await {target}.{e.Symbol.Name}({inputParam});");
							}
							else
							{
								emitter.AppendLine($"{target}.{e.Symbol.Name}({inputParam});");
							}
						}
						else
						{
							if (e.Subscribe.ChildOnly)
							{
								emitter.AppendLine($"foreach(var {target} in  FindParentStates<{attr.Type.ToDisplayString()}>())");
							}
							else
							{
								emitter.AppendLine($"foreach(var {target} in  Manager.FindAllStates<{attr.Type.ToDisplayString()}>())");
							}
							using (emitter.Brace())
							{
								if (e.IsAsync())
								{
									emitter.AppendLine($"await {target}.{e.Symbol.Name}({inputParam});");
								}
								else
								{
									emitter.AppendLine($"{target}.{e.Symbol.Name}({inputParam});");
								}
								if (!e.Subscribe.Broadcast)
								{
									emitter.AppendLine($"return;");
								}
							}
						}
					}

					string GetArg(IParameterSymbol symbol)
					{
						if (symbol.HasExplicitDefaultValue)
						{
							if (symbol.ExplicitDefaultValue == null)
							{
								return $"{symbol.Type.ToDisplayString()} {Escape(symbol.Name)} = default";
							}
							if (symbol.ExplicitDefaultValue is bool boolean)
							{
								if (boolean)
								{
									return $"{symbol.Type.ToDisplayString()} {Escape(symbol.Name)} = true";
								}
								else
								{
									return $"{symbol.Type.ToDisplayString()} {Escape(symbol.Name)} = false";
								}
							}
							if (symbol.ExplicitDefaultValue is string text)
							{
								return $"{symbol.Type.ToDisplayString()} {Escape(symbol.Name)} = \"{text}\"";
							}
							return $"{symbol.Type.ToDisplayString()} {Escape(symbol.Name)} = {symbol.ExplicitDefaultValue}";
						}
						return $"{symbol.Type.ToDisplayString()} {Escape(symbol.Name)}";
					}
				}
			}

			while (brace.Count > 0)
			{
				brace.Pop().Dispose();
			}
			context.AddSource(fileName, emitter.ToString());

		}

		private void GenerateTransMethod(GeneratorExecutionContext context, ISymbol symbol, TransAttribute attr)
		{
			TransAttribute.ChangeType changeType = attr.Type;

			StateData stateData = StateData.Create(attr.Symbol);

			string nameKey = attr.Name == null ? "none" : "name" + attr.Name;
			string fileName = $"{symbol.ToDisplayString()}.{attr.Type}.{stateData.Name}.{nameKey}.cs";

			CodeEmitter emitter = new();
			emitter.AppendLine("// <auto-generated />");
			emitter.AppendLine("using GameStateStructure;");
			emitter.AppendLine("using System.Threading;");
			emitter.AppendLine("using System.Threading.Tasks;");
			emitter.NewLine();

			Stack<IDisposable> brace = new();

			if (!symbol.ContainingNamespace.IsGlobalNamespace)
			{
				emitter.AppendLine($"namespace {symbol.ContainingNamespace.ToDisplayString()}");
				brace.Push(emitter.Brace());
			}

			Stack<INamedTypeSymbol> parent = new();
			INamedTypeSymbol nestType = symbol.ContainingType;
			while (nestType != null)
			{
				parent.Push(nestType);
				nestType = nestType.ContainingType;
			}
			while (parent.Count > 0)
			{
				INamedTypeSymbol p = parent.Pop();
				emitter.AppendLine($"partial class {p.Name}");
				brace.Push(emitter.Brace());
			}

			emitter.AppendLine($"partial class {symbol.Name}");
			using (emitter.Brace())
			{
				var argNames = new HashSet<string>(stateData.Args.Select(x => x.ArgName));
				string holder = Unique("parameter", argNames);
				string token = Unique("ct", argNames);

				IEnumerable<string> argsStrings = stateData.Args.Select(x => !x.Option
					? $"{x.Type} {Escape(x.ArgName)}"
					: $"Option<{x.Type}> {Escape(x.ArgName)} = default");
				if (stateData.IsProcess)
				{
					argsStrings = argsStrings.Append($"CancellationToken {token} = default");
				}

				string methodName = stateData.ShortName;
				if (attr.Name != null) // Use attribute name if it exists
				{
					methodName = attr.Name;
				}

				EmitConstructorPreserve(emitter, attr.Symbol, context.Compilation,
					methodSuffix: $"{attr.Type}{methodName}_{Identifier(stateData.Name)}");
				// sealed は型制約に使えない（CS0701）。型引数そのものを出さない
				bool useTypeParam = !(attr.Symbol is INamedTypeSymbol named && named.IsSealed);
				var typeName = stateData.ShortName;
				string typeDecl = useTypeParam ? $"<{typeName}>" : "";
				string typeConstraint = useTypeParam ? $" where {typeName} : {stateData.Name}" : "";
				string argParam = string.Join(", ", argsStrings);
				string branchArg = attr.Branch == null ? "null" : $"typeof({attr.Branch.ToDisplayString()})";
				if (stateData.IsProcess)
				{
					if (string.IsNullOrEmpty(stateData.Result))
					{
						emitter.AppendLine($"#if GSS_DISABLE_TYPE");
						emitter.AppendLine($"private Task Run{methodName}({argParam})");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"return Manager.RunProcess<{stateData.Name}>(this, {holder}, {branchArg}, {token});");
						}
						emitter.AppendLine($"#else");
						emitter.AppendLine($"private Task Run{methodName}{typeDecl}({argParam}){typeConstraint}");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"return Manager.RunProcess<{stateData.Name}>(this, {holder}, {branchArg}, {token});");
						}
						emitter.AppendLine($"#endif");
					}
					else
					{
						emitter.AppendLine($"#if GSS_DISABLE_TYPE");
						emitter.AppendLine($"private Task<{stateData.Result}> Run{methodName}({argParam})");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"return Manager.RunProcess<{stateData.Name}, {stateData.Result}>(this, {holder}, {branchArg}, {token});");
						}
						emitter.AppendLine($"#else");
						emitter.AppendLine($"private Task<{stateData.Result}> Run{methodName}{typeDecl}({argParam}){typeConstraint}");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"return Manager.RunProcess<{stateData.Name}, {stateData.Result}>(this, {holder}, {branchArg}, {token});");
						}
						emitter.AppendLine($"#endif");
					}
				}
				else
				{
					if (changeType == TransAttribute.ChangeType.GoTo)
					{
						emitter.AppendLine($"#if GSS_DISABLE_TYPE");
						emitter.AppendLine($"private void GoTo{methodName}({argParam})");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"Manager.GoTo<{stateData.Name}>(this, {holder});");
						}
						emitter.AppendLine($"#else");
						emitter.AppendLine($"private void GoTo{methodName}{typeDecl}({argParam}){typeConstraint}");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"Manager.GoTo<{stateData.Name}>(this, {holder});");
						}
						emitter.AppendLine($"#endif");
					}
					else
					{
						emitter.AppendLine($"#if GSS_DISABLE_TYPE");
						emitter.AppendLine($"private void Push{methodName}({argParam})");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"Manager.Push<{stateData.Name}>(this, {holder});");
						}
						emitter.AppendLine($"#else");
						emitter.AppendLine($"private void Push{methodName}{typeDecl}({argParam}){typeConstraint}");
						using (emitter.Brace())
						{
							EmitSetParam();
							emitter.AppendLine($"Manager.Push<{stateData.Name}>(this, {holder});");
						}
						emitter.AppendLine($"#endif");
					}
				}

				void EmitSetParam()
				{
					emitter.AppendLine($"var {holder} = new ParameterHolder();");
					foreach (StateArg arg in stateData.Args)
					{
						var name = Escape(arg.ArgName);
						if (!arg.Option)
						{
							emitter.AppendLine($"{holder}.Set(\"{arg.Name}\", {name});");
						}
						else
						{
							emitter.AppendLine($"if ({name}.HasValue)");
							using (emitter.Brace())
							{
								emitter.AppendLine($"{holder}.Set(\"{arg.Name}\", {name}.Value);");
							}
						}
					}
				}

			}

			while (brace.Count > 0)
			{
				brace.Pop().Dispose();
			}
			context.AddSource(fileName, emitter.ToString());
		}
	}
}