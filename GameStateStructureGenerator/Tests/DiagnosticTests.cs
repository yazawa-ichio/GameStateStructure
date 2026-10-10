using NUnit.Framework;
using System.Linq;

namespace GameStateStructure.Generator.Tests
{
	[TestFixture]
	public class DiagnosticTests
	{
		static void AssertOnly(string id, string source)
		{
			var result = GeneratorRunner.Run(source);
			CollectionAssert.AreEqual(new[] { id }, result.Ids.ToArray(), result.Describe());
		}

		static void AssertNone(string source)
		{
			var result = GeneratorRunner.Run(source);
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
		}

		// ---- GSS0001 partial ----

		[Test]
		public void NonPartialStateIsReported()
		{
			AssertOnly("GSS0001", @"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target))]
	class Host : GameState { }
}");
		}

		[Test]
		public void NonPartialOuterTypeIsReported()
		{
			// 生成コードは外側の型も partial として結合するため、外側も検査する
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	class Outer
	{
		[Push(typeof(Target))]
		partial class Host : GameState { }
	}
}");
			CollectionAssert.AreEqual(new[] { "GSS0001" }, result.Ids.ToArray(), result.Describe());
			StringAssert.Contains("Outer", result.Diagnostics[0].GetMessage());
		}

		[Test]
		public void PartialStateIsAccepted()
		{
			AssertNone(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0002 GameState 派生 ----

		[Test]
		public void NonGameStateTargetIsReported()
		{
			AssertOnly("GSS0002", @"
using GameStateStructure;
namespace Sample
{
	public class NotAState { }

	[Push(typeof(NotAState))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0003 生成可能性 ----

		[Test]
		public void AbstractTargetIsReported()
		{
			AssertOnly("GSS0003", @"
using GameStateStructure;
namespace Sample
{
	public abstract class Target : GameState { }

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void TargetWithConstructorDependenciesIsAccepted()
		{
			// IStateFactory がコンストラクタ依存を持つステートを生成できるため、
			// 引数なしコンストラクタの有無は検査しない。
			// ファクトリ未設定のまま使った場合は実行時に失敗する。
			AssertNone(@"
using GameStateStructure;
namespace Sample
{
	public interface IService { }

	public class Target : GameState
	{
		public Target(IService service) { }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0004 名前 ----

		[Test]
		public void InvalidNameIsReported()
		{
			AssertOnly("GSS0004", @"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target), ""my-name"")]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void EmptyNameIsAccepted()
		{
			// 空文字は「接頭辞のみの関数名にする」指定。
			// 以前は GoTo だけ GoTo<>() という構文エラーを生成していた。
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[GoTo(typeof(Target), """")]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			Assert.IsTrue(
				result.GeneratedSources.Any(x => x.Contains("void GoTo<Target>")),
				"GoTo<Target>() が生成される: " + string.Join("\n", result.GeneratedSources));
		}

		// ---- GSS0010 SubscribeEvent の可視性 ----

		[Test]
		public void NonPublicSubscriberIsReported()
		{
			AssertOnly("GSS0010", @"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		void Hidden() { }

		[SubscribeEvent]
		public void Visible() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0011 購読対象なし ----

		[Test]
		public void PublishEventWithoutSubscriberIsReported()
		{
			AssertOnly("GSS0011", @"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		public void NotSubscribed() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void PublishEventWithSubscriberIsAccepted()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public void Ping(int value) { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			Assert.IsTrue(
				result.GeneratedSources.Any(x => x.Contains("void Ping(int value)")),
				"発行関数が生成される: " + string.Join("\n", result.GeneratedSources));
		}

		// ---- GSS0012 Arg の setter ----

		[Test]
		public void ArgWithoutSetterIsReported()
		{
			AssertOnly("GSS0012", @"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public string Text { get; } = """";
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void ArgWithSetterIsAccepted()
		{
			AssertNone(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public string Text { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void PrivateSetterIsAccepted()
		{
			// ParameterHolder はリフレクションで代入するため private set でも動く
			AssertNone(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public string Text { get; private set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0006 生成関数の重複 ----

		[Test]
		public void DuplicateGeneratedMethodIsReported()
		{
			// 引数構成が同じステートを同じ Name で 2 つ宣言すると、
			// どちらも PushSame(...) になり CS0111 になる
			AssertOnly("GSS0006", @"
using GameStateStructure;
namespace Sample
{
	public class A : GameState { }
	public class B : GameState { }

	[Push(typeof(A), ""Same"")]
	[Push(typeof(B), ""Same"")]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void DifferentArgumentsDoNotCollide()
		{
			// 引数が違えばオーバーロードとして成立する
			AssertNone(@"
using GameStateStructure;
namespace Sample
{
	public class A : GameState { }

	public class B : GameState
	{
		[Arg]
		public string Text { get; set; }
	}

	[Push(typeof(A), ""Same"")]
	[Push(typeof(B), ""Same"")]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void SamePrefixWithDifferentNamesDoesNotCollide()
		{
			AssertNone(@"
using GameStateStructure;
namespace Sample
{
	public class A : GameState { }
	public class B : GameState { }

	[Push(typeof(A))]
	[Push(typeof(B))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void DuplicateEventMethodIsReported()
		{
			// 同名・同引数の購読関数を持つ 2 つの型を対象にすると発行関数が衝突する
			AssertOnly("GSS0006", @"
using GameStateStructure;
namespace Sample
{
	public class ListenerA
	{
		[SubscribeEvent]
		public void Ping(int value) { }
	}

	public class ListenerB
	{
		[SubscribeEvent]
		public void Ping(int value) { }
	}

	[PublishEvent(typeof(ListenerA))]
	[PublishEvent(typeof(ListenerB))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0013 購読関数の戻り値 ----

		[Test]
		public void DiscardedReturnTypeIsReported()
		{
			AssertOnly("GSS0013", @"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public int Ping() { return 0; }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
		}

		[TestCase("void")]
		[TestCase("System.Threading.Tasks.Task")]
		[TestCase("System.Threading.Tasks.ValueTask")]
		public void AwaitableAndVoidReturnTypesAreAccepted(string returnType)
		{
			var body = returnType == "void" ? "{ }" : "{ return default; }";
			AssertNone($@"
using GameStateStructure;
namespace Sample
{{
	public class Listener
	{{
		[SubscribeEvent]
		public {returnType} Ping() {body}
	}}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState {{ }}
}}");
		}

		// ---- SubscribeEvent の Internal ----

		[Test]
		public void InternalSubscriberDoesNotGeneratePublisher()
		{
			// ツールからの直接実行専用。発行関数は生成しない
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent(Internal = true)]
		public void DebugOnly() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.GeneratedSources.ToArray(), "発行関数は生成されない");
		}

		[Test]
		public void InternalOnlyDoesNotReportMissingSubscriber()
		{
			// Internal しか無い場合に GSS0011 を出すと誤検出になる
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent(Internal = true)]
		public void DebugOnly() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			Assert.IsFalse(result.Has("GSS0011"), result.Describe());
		}

		[Test]
		public void InternalAndNormalSubscribersCoexist()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent(Internal = true)]
		public void DebugOnly() { }

		[SubscribeEvent]
		public void Ping(int value) { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Join("", result.GeneratedSources);
			StringAssert.Contains("void Ping(int value)", generated);
			Assert.IsFalse(generated.Contains("DebugOnly"), "Internal は生成されない: " + generated);
		}

		[Test]
		public void TransitionMetadataDoesNotAffectGeneration()
		{
			// Transition はメタデータのみ。生成には影響しない
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	public class Listener
	{
		[SubscribeEvent(Transition = typeof(Target))]
		public void Go() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			StringAssert.Contains("void Go()", string.Join("", result.GeneratedSources));
		}

		// ---- GSS0020 / GSS0021 SubscribeEvent の形 ----

		[Test]
		public void StaticSubscribeEventIsReported()
		{
			// static はインスタンス経由で呼べず、生成コードが壊れる
			AssertOnly("GSS0020", @"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public static void Ping() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void GenericSubscribeEventIsReported()
		{
			AssertOnly("GSS0021", @"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public void Ping<T>() { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0023 は GSS_STRICT_EVENT 指定時のみ ----

		const string UneditableParameterSource = @"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public void Ping(GameState state) { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}";

		[Test]
		public void UneditableParameterIsNotReportedByDefault()
		{
			// 本番の API として複雑な引数を取るイベントは正常。既定では報告しない
			var result = GeneratorRunner.Run(UneditableParameterSource);
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
		}

		[Test]
		public void UneditableParameterIsReportedWithStrictSymbol()
		{
			var result = GeneratorRunner.Run(UneditableParameterSource, "GSS_STRICT_EVENT");
			CollectionAssert.AreEqual(new[] { "GSS0023" }, result.Ids.ToArray(), result.Describe());
		}

		[Test]
		public void RefParameterIsReportedWithStrictSymbol()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public void Ping(ref int value) { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}", "GSS_STRICT_EVENT");
			CollectionAssert.AreEqual(new[] { "GSS0023" }, result.Ids.ToArray(), result.Describe());
		}

		[Test]
		public void EditableParametersAreAcceptedUnderStrictSymbol()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	enum Mode { A, B }

	public class Listener
	{
		[SubscribeEvent]
		public void Ping(bool flag, int count, float ratio, string text, Mode mode) { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}", "GSS_STRICT_EVENT");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
		}

		// ---- エラー時は生成しない ----

		[Test]
		public void NothingIsGeneratedWhenDiagnosticIsAnError()
		{
			// 生成物由来の CS エラーで原因が埋もれないよう、エラー時は生成をスキップする
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class NotAState { }

	[Push(typeof(NotAState))]
	partial class Host : GameState { }
}");
			Assert.IsTrue(result.Has("GSS0002"), result.Describe());
			CollectionAssert.IsEmpty(result.GeneratedSources.ToArray());
		}

		// ---- GSS0030 Branch の対象は IProcess ----

		[Test]
		public void BranchTargetThatIsNotProcessIsReported()
		{
			AssertOnly("GSS0030", @"
using GameStateStructure;
namespace Sample
{
	public class HudBranch : Branch { }

	public class Target : GameState { }

	[Push(typeof(Target), Branch = typeof(HudBranch))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void BranchTargetInheritingProcessFromBaseIsAccepted()
		{
			// AllInterfaces で見るため、基底で実装された IProcess も拾える
			AssertNone(@"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class HudBranch : Branch { }

	public abstract class ProcessBase : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	public class Target : ProcessBase { }

	[Push(typeof(Target), Branch = typeof(HudBranch))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0031 Branch の指定 ----

		[Test]
		public void BranchThatDoesNotDeriveFromBranchIsReported()
		{
			AssertOnly("GSS0031", @"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class NotABranch { }

	public class Target : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[Push(typeof(Target), Branch = typeof(NotABranch))]
	partial class Host : GameState { }
}");
		}

		[Test]
		public void AbstractBranchIsReported()
		{
			AssertOnly("GSS0031", @"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public abstract class HudBranch : Branch { }

	public class Target : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[Push(typeof(Target), Branch = typeof(HudBranch))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0032 GoTo の対象に IProcess ----

		[Test]
		public void GoToTargetingProcessIsReported()
		{
			AssertOnly("GSS0032", @"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class Target : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[GoTo(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		// ---- GSS0034 IProcess は GoTo を宣言できない ----

		[Test]
		public void ProcessDeclaringGoToIsReported()
		{
			AssertOnly("GSS0034", @"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class Target : GameState { }

	[GoTo(typeof(Target))]
	partial class Host : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}
}");
		}

		[Test]
		public void ProcessDeclaringPushIsAccepted()
		{
			AssertNone(@"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target))]
	partial class Host : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}
}");
		}

		// ---- コンストラクタの保持 ----

		[Test]
		public void ConstructorWithParametersIsPreserved()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public interface IService { }

	public class Target : GameState
	{
		public Target(IService service) { }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("UnityEngine.Scripting.Preserve", generated);
			StringAssert.Contains("new Sample.Target(default(Sample.IService))", generated);
		}

		[Test]
		public void ImplicitParameterlessConstructorIsPreserved()
		{
			// 宣言していない暗黙のコンストラクタも除去される（実測）ため対象にする
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			StringAssert.Contains("new Sample.Target()", string.Concat(result.GeneratedSources));
		}

		[Test]
		public void EveryAccessibleConstructorIsPreserved()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public interface IService { }

	public class Target : GameState
	{
		public Target() { }
		public Target(IService service) { }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("new Sample.Target()", generated);
			StringAssert.Contains("new Sample.Target(default(Sample.IService))", generated);
		}

		[Test]
		public void ConstructorWithUnreferencedParameterTypeIsSkipped()
		{
			// 宣言側が参照していないアセンブリの型は書けない。
			// 書くと生成ファイルの中で CS0012 になりビルドが壊れる。
			var result = GeneratorRunner.RunWithReferences(@"
using GameStateStructure;
namespace Sample
{
	[Push(typeof(Near.Target))]
	partial class Host : GameStateStructure.GameState { }
}",
				far: @"
namespace Far
{
	public interface IRanking { }
}",
				near: @"
namespace Near
{
	public class Target : GameStateStructure.GameState
	{
		public Target(Far.IRanking ranking) { }
	}
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.DoesNotContain("Far.IRanking", generated);
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void ConstructorWithReferencedParameterTypeIsPreserved()
		{
			// 参照している側は従来どおり対象になる（上の除外が効きすぎていないこと）
			var result = GeneratorRunner.RunWithReferences(@"
using GameStateStructure;
namespace Sample
{
	[Push(typeof(Near.Target))]
	partial class Host : GameStateStructure.GameState { }
}",
				far: @"
namespace Far
{
	public interface IRanking { }
}",
				near: @"
namespace Near
{
	public interface IService { }

	public class Target : GameStateStructure.GameState
	{
		public Target(IService service) { }
	}
}");
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("new Near.Target(default(Near.IService))", generated);
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void UnnamedAndEmptyNameOnTheSameTargetCoexist()
		{
			// Name 未指定と Name = "" は生成関数名が別（PushTarget / Push）になる。
			// 生成ファイル名でも区別できているか
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target))]
	[Push(typeof(Target), """")]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("PushTarget", generated);
		}

		// ---- 生成コードが使う識別子と [Arg] 名の衝突 ----

		[Test]
		public void ArgNamedParameterDoesNotCollideWithTheHolder()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public int Parameter { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void OptionalArgNamedParameterDoesNotCollideWithTheHolder()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg(Option = true)]
		public int Parameter { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void ArgNamedCtDoesNotCollideWithTheCancellationToken()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class Target : GameState, IProcess
	{
		[Arg]
		public int Ct { get; set; }

		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void ArgNamedAfterAKeywordIsEscaped()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public int Class { get; set; }

		[Arg]
		public string Event { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void ArgNamedThisIsEscaped()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public int This { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void EventParameterNamedStateDoesNotCollide()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public void Ping(int state) { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void EventParameterNamedAfterAKeywordIsEscaped()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Listener
	{
		[SubscribeEvent]
		public void Ping(int @event, string @class = ""x"") { }
	}

	[PublishEvent(typeof(Listener))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		// ---- 基底に付けた [Arg] ----

		[Test]
		public void ArgOnTheBaseTypeBecomesAParameter()
		{
			// 実行時の代入は基底のプロパティも拾う。生成側が宣言型だけを見ると引数が消える
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public abstract class Base : GameState
	{
		[Arg]
		public int Day { get; set; }
	}

	public class Target : Base
	{
		[Arg]
		public string Text { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("int day", generated, "基底の [Arg] も引数になる");
			StringAssert.Contains("string text", generated);
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void DerivedArgsComeBeforeBaseArgs()
		{
			// 基底の引数を後ろに置く。先頭に挿すと既存の呼び出しが黙って別の意味になる
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public abstract class Base : GameState
	{
		[Arg]
		public int BaseValue { get; set; }
	}

	public class Target : Base
	{
		[Arg]
		public int DerivedValue { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("int derivedValue, int baseValue", generated);
		}

		[Test]
		public void ArgHiddenByTheDerivedTypeIsNotDuplicated()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public abstract class Base : GameState
	{
		[Arg]
		public virtual int Day { get; set; }
	}

	public class Target : Base
	{
		[Arg]
		public override int Day { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"同じ名前の引数が 2 つ出ない");
		}

		[Test]
		public void ArgOnTheBaseTypeWithoutSetterIsReported()
		{
			AssertOnly("GSS0012", @"
using GameStateStructure;
namespace Sample
{
	public abstract class Base : GameState
	{
		[Arg]
		public int Day { get; } = 1;
	}

	public class Target : Base { }

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
		}

		// ---- sealed な遷移先 ----

		[Test]
		public void SealedTargetDoesNotGetATypeParameter()
		{
			// sealed は型制約に使えない（CS0701）
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public sealed class Target : GameState { }

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("void PushTarget()", generated, "型引数を付けない");
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void SealedProcessTargetDoesNotGetATypeParameter()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public sealed class Target : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			CollectionAssert.IsEmpty(result.CompileErrorTexts.ToArray(),
				"生成コードがそのままコンパイルできること");
		}

		[Test]
		public void ConstructorPreserveCanBeDisabled()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public interface IService { }

	public class Target : GameState
	{
		public Target(IService service) { }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			// 生成物にはシンボルによる切り替えが入っている
			StringAssert.Contains("#if !GSS_DISABLE_CONSTRUCTOR_PRESERVE", string.Concat(result.GeneratedSources));
		}

		[Test]
		public void SameShortNameTargetsDoNotCollide()
		{
			// 名前空間だけが違う同名の型を 2 つ遷移先にする
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace A { public class Screen : GameState { } }
namespace B { public class Screen : GameState { } }
namespace Sample
{
	[Push(typeof(A.Screen), ""First"")]
	[Push(typeof(B.Screen), ""Second"")]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("PreserveConstructorPushFirst_A_Screen(", generated);
			StringAssert.Contains("PreserveConstructorPushSecond_B_Screen(", generated);
		}

		[Test]
		public void SameNameOverloadsDoNotCollide()
		{
			// 引数が違えば同じ Name を使えるのは正当なオーバーロード
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class WithText : GameState
	{
		[Arg]
		public string Text { get; set; }
	}

	public class WithValue : GameState
	{
		[Arg]
		public int Value { get; set; }
	}

	[Push(typeof(WithText), ""Open"")]
	[Push(typeof(WithValue), ""Open"")]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("PreserveConstructorPushOpen_Sample_WithText(", generated);
			StringAssert.Contains("PreserveConstructorPushOpen_Sample_WithValue(", generated);
		}

		[Test]
		public void SameTargetWithDifferentNamesGenerates()
		{
			// 同じ型を別の Name で 2 回宣言できる。生成ファイル名が衝突すると
			// 生成そのものが GSS0900 で失敗する
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState { }

	[Push(typeof(Target), ""First"")]
	[Push(typeof(Target), ""Second"")]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Concat(result.GeneratedSources);
			StringAssert.Contains("PushFirst", generated);
			StringAssert.Contains("PushSecond", generated);
		}

		// ---- 生成内容 ----

		[Test]
		public void BranchIsPassedToRunProcess()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class HudBranch : Branch { }

	public class Target : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[Push(typeof(Target), Branch = typeof(HudBranch))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Join("\n", result.GeneratedSources);
			StringAssert.Contains("RunTarget", generated);
			StringAssert.Contains("typeof(Sample.HudBranch)", generated);
		}

		[Test]
		public void MainAxisProcessPassesNullBranch()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
using System.Threading;
using System.Threading.Tasks;
namespace Sample
{
	public class Target : GameState, IProcess
	{
		public Task Run(CancellationToken ct) => Task.CompletedTask;
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			StringAssert.Contains("parameter, null, ct", string.Join("\n", result.GeneratedSources));
		}

		// ---- 正常系 ----

		[Test]
		public void ValidDeclarationGeneratesTransitionMethod()
		{
			var result = GeneratorRunner.Run(@"
using GameStateStructure;
namespace Sample
{
	public class Target : GameState
	{
		[Arg]
		public string Text { get; set; }

		[Arg(Option = true)]
		public int Retry { get; set; }
	}

	[Push(typeof(Target))]
	partial class Host : GameState { }
}");
			CollectionAssert.IsEmpty(result.Ids.ToArray(), result.Describe());
			var generated = string.Join("\n", result.GeneratedSources);
			StringAssert.Contains("PushTarget", generated);
			StringAssert.Contains("Option<int> retry", generated);
		}
	}
}
