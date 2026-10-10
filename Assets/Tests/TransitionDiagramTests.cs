using GameStateStructure.Diagnostics;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// 宣言された遷移から mermaid を生成する処理の検証。
	/// </summary>
	public partial class TransitionDiagramTests
	{
		[Push(typeof(Child), "Child")]
		[GoTo(typeof(Sibling), "Sibling")]
		[Push(typeof(Dialog), "Dialog")]
		partial class Origin : GameState
		{
		}

		class Child : GameState
		{
		}

		class Sibling : GameState
		{
		}

		class Dialog : GameState, IProcess<int>
		{
			public Task<int> Run(CancellationToken ct) => Task.FromResult(0);
		}

		static string[] Lines(string text)
			=> text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

		[Test]
		public void EdgesAreEmittedPerTransitionKind()
		{
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(Origin) }));

			Assert.AreEqual("flowchart LR", lines[0]);
			Assert.IsTrue(lines.Any(x => x.Contains("-->|Push|") && x.Contains("Child")), string.Join("\n", lines));
			Assert.IsTrue(lines.Any(x => x.Contains("-.->|GoTo|") && x.Contains("Sibling")), string.Join("\n", lines));
			Assert.IsTrue(lines.Any(x => x.Contains("==>|Process|") && x.Contains("Dialog")), string.Join("\n", lines));
		}

		[Test]
		public void ProcessStateIsMarked()
		{
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(Origin) }));

			Assert.IsTrue(
				lines.Any(x => x.Contains("[\"Dialog\"]") && x.EndsWith(":::process")),
				string.Join("\n", lines));
			Assert.IsTrue(
				lines.Any(x => x.Contains("[\"Child\"]") && !x.Contains(":::process")),
				string.Join("\n", lines));
		}

		[Test]
		public void TargetsWithoutOutgoingEdgesAreStillListed()
		{
			// 遷移先として現れるだけの型もノードになる
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(Origin) }));

			Assert.IsTrue(lines.Any(x => x.Contains("[\"Sibling\"]")), string.Join("\n", lines));
		}

		// 循環する宣言。README の Root や TransitionTest の MainA/MainB が実例
		[GoTo(typeof(Loop), "Self")]
		[Push(typeof(Mutual), "Mutual")]
		partial class Loop : GameState
		{
		}

		[GoTo(typeof(Loop), "Back")]
		partial class Mutual : GameState
		{
		}

		[Test]
		public void SelfLoopAndMutualTransitionsAreEmittedOnce()
		{
			// 循環しても無限ループせず、辺は重複しない
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(Loop), typeof(Mutual) }));

			var edges = lines.Where(x => x.Contains("|")).ToArray();
			var detail = string.Join(" / ", edges);
			Assert.AreEqual(edges.Length, edges.Distinct().Count(), "辺が重複しない: " + detail);
			Assert.IsTrue(edges.Any(x => x.Contains("Loop") && x.Contains("-.->|GoTo|")), detail);
			Assert.AreEqual(3, edges.Length, "自己ループ 1 + 相互 2: " + detail);
		}

		[Test]
		public void OutputIsStableRegardlessOfInputOrder()
		{
			var a = TransitionDiagram.GenerateMermaid(new[] { typeof(Origin), typeof(Child) });
			var b = TransitionDiagram.GenerateMermaid(new[] { typeof(Child), typeof(Origin) });

			Assert.AreEqual(a, b, "差分を取れるよう出力は安定させる");
		}

		class SideBranch : Branch
		{
		}

		[Push(typeof(Dialog), "Side", Branch = typeof(SideBranch))]
		partial class Branched : GameState
		{
		}

		// 遷移先が IProcess でない場合。属性としては GSS0030 で弾かれるが、
		// 図の描画は遷移先の種類に関わらず車線を優先する
		class PlainChild : GameState
		{
		}

		[Push(typeof(PlainChild), "Plain")]
		partial class BranchedPlain : GameState
		{
		}

		[Test]
		public void BranchIsShownOnTheEdge()
		{
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(Branched) }));

			Assert.IsTrue(
				lines.Any(x => x.Contains("==>|Branch:SideBranch|") && x.Contains("Dialog")),
				string.Concat(lines));
		}

		partial class EventDriven : GameState
		{
			[SubscribeEvent(Internal = true, Transition = typeof(Sibling))]
			public void Reboot()
			{
			}
		}

		[Test]
		public void BranchIsShownEvenWhenTargetIsNotProcess()
		{
			// 属性からは作れない組み合わせなので、辺を直接組み立てて描画規則だけを見る
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(BranchedPlain) }));

			Assert.IsTrue(lines.Any(x => x.Contains("-->|Push|") && x.Contains("PlainChild")),
				string.Concat(lines));
		}

		[Test]
		public void EventTransitionIsShown()
		{
			// 属性で宣言されない遷移も、Transition が書かれていれば図に出る
			var lines = Lines(TransitionDiagram.GenerateMermaid(new[] { typeof(EventDriven) }));

			Assert.IsTrue(lines.Any(x => x.Contains("[\"Sibling\"]")), string.Concat(lines));
			Assert.IsTrue(
				lines.Any(x => x.Contains("-.->|Event:Reboot|") && x.Contains("Sibling")),
				string.Concat(lines));
		}

		[Test]
		public void EmptyInputProducesHeaderOnly()
		{
			var lines = Lines(TransitionDiagram.GenerateMermaid(Array.Empty<Type>()));

			Assert.AreEqual("flowchart LR", lines[0]);
			Assert.IsFalse(lines.Any(x => x.Contains("-->")), string.Join("\n", lines));
		}
	}
}
