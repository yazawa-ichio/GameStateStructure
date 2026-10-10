using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameStateStructure.Generator.Tests
{
	internal sealed class GeneratorResult
	{
		public IReadOnlyList<Diagnostic> Diagnostics { get; init; }

		public IReadOnlyList<string> GeneratedSources { get; init; }

		/// <summary>生成物を足したコンパイルのエラー。生成コードが通るかの検証に使う。</summary>
		public IReadOnlyList<Diagnostic> CompileErrors { get; init; }

		public IEnumerable<string> CompileErrorTexts => CompileErrors.Select(x => x.ToString());

		public IEnumerable<string> Ids => Diagnostics.Select(x => x.Id);

		public bool Has(string id) => Ids.Contains(id);

		public string Describe()
		{
			if (Diagnostics.Count == 0)
			{
				return "(診断なし)";
			}
			return string.Join(", ", Diagnostics.Select(x => $"{x.Id}: {x.GetMessage()}"));
		}
	}

	/// <summary>
	/// SourceGenerator を Roslyn 上で直接実行する。
	/// 診断を発火させるコードは Unity のテストアセンブリに置けない
	/// （コンパイルが壊れる）ため、こちらで検証する。
	/// </summary>
	internal static class GeneratorRunner
	{
		/// <summary>
		/// ランタイムの実物ではなく、ジェネレータが参照する型だけを再現したスタブ。
		/// ランタイム本体は UnityEngine に依存するため直接は使えない。
		/// </summary>
		const string Stub = @"
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure
{
	public abstract class GameState
	{
		public GameStateManager Manager { get; internal set; }
		public bool Active => false;
		protected T FindParentState<T>() where T : class => null;
		protected System.Collections.Generic.IEnumerable<T> FindParentStates<T>() where T : class => Array.Empty<T>();
	}

	public class ParameterHolder
	{
		public void Set(string key, object value) { }
	}

	public class GameStateManager
	{
		public void Push<T>(GameState current, ParameterHolder parameter) where T : GameState { }
		public void GoTo<T>(GameState current, ParameterHolder parameter) where T : GameState { }
		public Task RunProcess<T>(GameState current, ParameterHolder parameter, Type branch, CancellationToken ct) where T : GameState, IProcess => Task.CompletedTask;
		public Task<TResult> RunProcess<T, TResult>(GameState current, ParameterHolder parameter, Type branch, CancellationToken ct) where T : GameState, IProcess<TResult> => Task.FromResult<TResult>(default);
		public System.Collections.Generic.IEnumerable<T> FindAllStates<T>() where T : class => Array.Empty<T>();
	}

	public interface IProcess { Task Run(CancellationToken ct); }

	public interface IProcess<TResult> { Task<TResult> Run(CancellationToken ct); }

	public readonly struct Option<T>
	{
		public static implicit operator Option<T>(T value) => default;
		public bool HasValue => false;
		public T Value => default;
	}

	[AttributeUsage(AttributeTargets.Property)]
	public sealed class ArgAttribute : Attribute { public bool Option { get; set; } }

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class GoToAttribute : Attribute
	{
		public GoToAttribute(Type type) { }
		public GoToAttribute(Type type, string name) { }
		public string Name { get; private set; }
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class PushAttribute : Attribute
	{
		public PushAttribute(Type type) { }
		public PushAttribute(Type type, string name) { }
		public string Name { get; private set; }
		public Type Branch { get; set; }
	}

	public enum BranchOccupied { Replace, Reject }

	public abstract class Branch
	{
		public virtual BranchOccupied Occupied => BranchOccupied.Replace;
	}

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface)]
	public sealed class SubscribeEventAttribute : Attribute
	{
		public bool Broadcast { get; set; }
		public bool ChildOnly { get; set; }
		public bool Internal { get; set; }
		public Type Transition { get; set; }
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class PublishEventAttribute : Attribute
	{
		public PublishEventAttribute(Type type) { }
		public string Prefix { get; set; }
	}

}

namespace UnityEngine.Scripting
{
	[AttributeUsage(AttributeTargets.All)]
	public class PreserveAttribute : Attribute { }
}
";

		static readonly MetadataReference[] s_References = CreateReferences();

		static MetadataReference[] CreateReferences()
		{
			var assemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
			return assemblies
				.Split(Path.PathSeparator)
				.Where(x => !string.IsNullOrEmpty(x))
				.Select(x => (MetadataReference)MetadataReference.CreateFromFile(x))
				.ToArray();
		}

		/// <summary>
		/// <paramref name="defines"/> にシンボルを渡すと、そのシンボルが定義された状態で生成する。
		/// GSS_STRICT_EVENT のような opt-in の診断を検証するために使う。
		/// </summary>
		public static GeneratorResult Run(string source, params string[] defines)
		{
			var parseOptions = new CSharpParseOptions(preprocessorSymbols: defines);
			var trees = new[]
			{
				CSharpSyntaxTree.ParseText(Stub, parseOptions),
				CSharpSyntaxTree.ParseText(source, parseOptions),
			};
			var compilation = CSharpCompilation.Create(
				"GeneratorTest",
				trees,
				s_References,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
			return Execute(compilation, parseOptions);
		}

		/// <summary>
		/// 別アセンブリを絡めて生成する。
		/// <para>
		/// <paramref name="far"/> は <paramref name="near"/> だけが参照し、
		/// 検証対象のコンパイルからは参照しない。宣言側が参照していない
		/// アセンブリの型を生成コードに書いてしまう不具合を検出するために使う。
		/// </para>
		/// </summary>
		public static GeneratorResult RunWithReferences(string source, string far, string near)
		{
			var parseOptions = new CSharpParseOptions();
			var stubReference = CompileToReference("GameStateStructure.Stub", Stub, Array.Empty<MetadataReference>());
			var farReference = CompileToReference("Far", far, new[] { stubReference });
			var nearReference = CompileToReference("Near", near, new[] { stubReference, farReference });
			var compilation = CSharpCompilation.Create(
				"GeneratorTest",
				new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
				// Far は意図的に外す
				s_References.Concat(new[] { stubReference, nearReference }),
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
			return Execute(compilation, parseOptions);
		}

		static GeneratorResult Execute(CSharpCompilation compilation, CSharpParseOptions parseOptions)
		{
			var driver = CSharpGeneratorDriver.Create(
				new[] { (ISourceGenerator)new GameStateStructureSourceGenerator() },
				null,
				parseOptions,
				null);
			var result = driver.RunGenerators(compilation).GetRunResult();

			var generated = compilation.AddSyntaxTrees(result.GeneratedTrees);
			return new GeneratorResult
			{
				Diagnostics = result.Diagnostics.ToArray(),
				GeneratedSources = result.GeneratedTrees.Select(x => x.ToString()).ToArray(),
				CompileErrors = generated.GetDiagnostics()
					.Where(x => x.Severity == DiagnosticSeverity.Error)
					.ToArray(),
			};
		}

		static MetadataReference CompileToReference(string name, string source, MetadataReference[] extra)
		{
			var compilation = CSharpCompilation.Create(
				name,
				new[] { CSharpSyntaxTree.ParseText(source) },
				s_References.Concat(extra),
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
			using var stream = new MemoryStream();
			var result = compilation.Emit(stream);
			if (!result.Success)
			{
				throw new InvalidOperationException($"{name}: " + string.Join(
					", ",
					result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Select(x => x.ToString())));
			}
			return MetadataReference.CreateFromImage(stream.ToArray());
		}
	}
}
