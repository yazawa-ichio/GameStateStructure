using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// ライフサイクルコールバックの呼び出し順序を検証する。
	/// 順序は Recorder に文字列で積み、まとめて比較する。
	/// </summary>
	public partial class LifecycleTests
	{
		class Recorder
		{
			public List<string> Log { get; } = new();

			public Dictionary<string, int> UpdateCounts { get; } = new();

			public void Add(string entry) => Log.Add(entry);

			public void CountUpdate(string label)
			{
				UpdateCounts.TryGetValue(label, out var count);
				UpdateCounts[label] = count + 1;
			}

			public int UpdateCountOf(string label)
			{
				UpdateCounts.TryGetValue(label, out var count);
				return count;
			}

			public void Clear() => Log.Clear();

			public string Joined => string.Join(",", Log);
		}

		// ステートは Manager が生成するためコンストラクタ経由で渡せない。
		// テストごとに SetUp で作り直す。
		static Recorder s_Recorder;

		[SetUp]
		public void SetUp()
		{
			s_Recorder = new Recorder();
		}

		abstract class RecordingState : GameState
		{
			protected string Label => GetType().Name;

			protected override Task OnInitialize()
			{
				s_Recorder.Add($"{Label}.Initialize");
				return Task.CompletedTask;
			}

			protected override Task OnPreEnter()
			{
				s_Recorder.Add($"{Label}.PreEnter");
				return Task.CompletedTask;
			}

			protected override void OnEnter()
			{
				s_Recorder.Add($"{Label}.Enter");
			}

			protected override Task OnPreExit()
			{
				s_Recorder.Add($"{Label}.PreExit");
				return Task.CompletedTask;
			}

			protected override Task OnExit()
			{
				s_Recorder.Add($"{Label}.Exit");
				return Task.CompletedTask;
			}

			protected override void OnUpdate()
			{
				s_Recorder.CountUpdate(Label);
			}

			protected override void OnPopChild(GameState state, PopReason reason)
			{
				s_Recorder.Add($"{Label}.PopChild({state.GetType().Name},{reason})");
			}
		}

		[GoTo(typeof(Alpha), "Alpha")]
		[Push(typeof(Beta), "Beta")]
		[Push(typeof(Delta), "Delta")]
		partial class Alpha : RecordingState
		{
			public void DoPushBeta() => PushBeta<Beta>();

			public void DoPushDelta() => PushDelta<Delta>();

			public void DoGoToAlpha() => GoToAlpha<Alpha>();
		}

		[Push(typeof(Gamma), "Gamma")]
		[GoTo(typeof(Delta), "Delta")]
		partial class Beta : RecordingState
		{
			public void DoPushGamma() => PushGamma<Gamma>();

			public void DoGoToDelta() => GoToDelta<Delta>();

			public void DoPop() => Pop();
		}

		class Gamma : RecordingState
		{
		}

		class Delta : RecordingState
		{
		}

		[Test]
		public async Task EntryRunsInitializeThenPreEnterThenEnter()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();

			Assert.AreEqual("Alpha.Initialize,Alpha.PreEnter,Alpha.Enter", s_Recorder.Joined);
		}

		[Test]
		public async Task PushRunsChildLifecycleOnly()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			s_Recorder.Clear();

			context.Manager.FindState<Alpha>().DoPushBeta();
			await context.WaitTransition();

			// 親のコールバックは一切呼ばれない
			Assert.AreEqual("Beta.Initialize,Beta.PreEnter,Beta.Enter", s_Recorder.Joined);
		}

		[Test]
		public async Task PopRunsPreExitThenExitThenNotifiesParent()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			context.Manager.FindState<Alpha>().DoPushBeta();
			await context.WaitTransition();
			s_Recorder.Clear();

			context.Manager.FindState<Beta>().DoPop();
			await context.WaitTransition();

			Assert.AreEqual("Beta.PreExit,Beta.Exit,Alpha.PopChild(Beta,Pop)", s_Recorder.Joined);
		}

		[Test]
		public async Task DescendantsExitBeforeTheirParent()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			context.Manager.FindState<Alpha>().DoPushBeta();
			await context.WaitTransition();
			context.Manager.FindState<Beta>().DoPushGamma();
			await context.WaitTransition();
			s_Recorder.Clear();

			context.Manager.FindState<Beta>().DoPop();
			await context.WaitTransition();

			// 孫が完全に終了してから子が終了する。
			// 自身も破棄される途中の Beta にも Gamma の破棄が ParentExit として通知される。
			Assert.AreEqual(
				"Gamma.PreExit,Gamma.Exit,Beta.PopChild(Gamma,ParentExit),Beta.PreExit,Beta.Exit,Alpha.PopChild(Beta,Pop)",
				s_Recorder.Joined);
		}

		[Test]
		public async Task GoToExitsOutgoingStateExactlyOnce()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			s_Recorder.Clear();

			context.Manager.FindState<Alpha>().DoGoToAlpha();
			await context.WaitTransition();

			// 新ステートの Initialize が先に走り、その後に旧ステートが 1 度だけ終了する。
			Assert.AreEqual(
				"Alpha.Initialize,Alpha.PreExit,Alpha.Exit,Alpha.PreEnter,Alpha.Enter",
				s_Recorder.Joined);
		}

		[Test]
		public async Task UpdateRunsOnlyWhileActive()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			context.Manager.FindState<Alpha>().DoPushBeta();
			await context.WaitTransition();

			var beta = context.Manager.FindState<Beta>();
			Assert.IsTrue(beta.Active);

			await context.Tick(5);
			Assert.Greater(s_Recorder.UpdateCountOf("Beta"), 0, "Active な間は OnUpdate が呼ばれる");

			beta.DoPop();
			await context.WaitTransition();
			await context.Tick(1);

			var afterPop = s_Recorder.UpdateCountOf("Beta");
			await context.Tick(5);
			Assert.AreEqual(afterPop, s_Recorder.UpdateCountOf("Beta"), "破棄後は OnUpdate が呼ばれない");
		}

		[Test]
		public async Task PopReasonIsGoToWhenChildIsReplaced()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			context.Manager.FindState<Alpha>().DoPushBeta();
			await context.WaitTransition();
			s_Recorder.Clear();

			context.Manager.FindState<Beta>().DoGoToDelta();
			await context.WaitTransition();

			CollectionAssert.Contains(s_Recorder.Log, "Alpha.PopChild(Beta,GoTo)");
		}

		[Test]
		public async Task PopReasonIsReplaceWhenSiblingIsPushed()
		{
			using var context = new TestContext();
			await context.Entry<Alpha>();
			context.Manager.FindState<Alpha>().DoPushBeta();
			await context.WaitTransition();
			s_Recorder.Clear();

			// 主軸は排他なので、兄弟を Push すると既存の子が置き換えられる
			context.Manager.FindState<Alpha>().DoPushDelta();
			await context.WaitTransition();

			CollectionAssert.Contains(s_Recorder.Log, "Alpha.PopChild(Beta,Replace)");
		}
	}
}
