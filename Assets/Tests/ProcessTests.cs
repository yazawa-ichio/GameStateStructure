using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// IProcess / IProcess&lt;TResult&gt; の検証。
	/// 特に戻り値付きの IProcess&lt;TResult&gt; は既存テストに 1 件も無かった。
	/// </summary>
	public partial class ProcessTests
	{
		[Push(typeof(Middle), "Middle")]
		[Push(typeof(Sibling), "Sibling")]
		[Push(typeof(ResultProcess), "Result")]
		[Push(typeof(GatedProcess), "Gated")]
		[Push(typeof(ThrowingProcess), "Throwing")]
		[Push(typeof(InitializeThrowingProcess), "InitThrowing")]
		partial class Host : GameState
		{
			public List<string> Popped { get; } = new();

			protected override void OnPopChild(GameState state, PopReason reason)
				=> Popped.Add($"{state.GetType().Name},{reason}");

			public Task<int> DoRunResult(int seed) => RunResult<ResultProcess>(seed);

			public Task<string> DoRunGated(TaskCompletionSource<string> gate, CancellationToken ct = default)
				=> RunGated<GatedProcess>(gate, ct);

			public Task DoRunThrowing() => RunThrowing<ThrowingProcess>();

			public Task DoRunInitThrowing() => RunInitThrowing<InitializeThrowingProcess>();

			public void DoPushMiddle() => PushMiddle<Middle>();

			public void DoPushSibling() => PushSibling<Sibling>();
		}

		/// <summary>子に Process を走らせる中間のステート。</summary>
		[Push(typeof(GatedProcess), "Gated")]
		[Push(typeof(InitGatedProcess), "InitGated")]
		partial class Middle : GameState
		{
			public Task<string> Running { get; private set; }

			public Task RunningInitGated { get; private set; }

			public void DoRunGated(TaskCompletionSource<string> gate)
				=> Running = RunGated<GatedProcess>(gate);

			public void DoRunInitGated(InitGate gate)
				=> RunningInitGated = RunInitGated<InitGatedProcess>(gate);

			public void DoPop() => Pop();
		}

		class Sibling : GameState
		{
		}

		partial class ResultProcess : GameState, IProcess<int>
		{
			[Arg]
			public int Seed { get; set; }

			public Task<int> Run(CancellationToken ct)
			{
				return Task.FromResult(Seed * 2);
			}
		}

		partial class GatedProcess : GameState, IProcess<string>
		{
			[Arg]
			public TaskCompletionSource<string> Gate { get; set; }

			public Task<string> Run(CancellationToken ct)
			{
				ct.Register(() => Gate.TrySetCanceled(ct));
				return Gate.Task;
			}
		}

		class InitGate
		{
			public TaskCompletionSource<bool> Initialize { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public bool Ran { get; set; }
		}

		partial class InitGatedProcess : GameState, IProcess
		{
			[Arg]
			public InitGate Gate { get; set; }

			protected override Task OnInitialize() => Gate.Initialize.Task;

			public Task Run(CancellationToken ct)
			{
				Gate.Ran = true;
				return Task.CompletedTask;
			}
		}

		partial class ThrowingProcess : GameState, IProcess
		{
			public Task Run(CancellationToken ct)
			{
				throw new InvalidOperationException("run failed");
			}
		}

		partial class InitializeThrowingProcess : GameState, IProcess
		{
			protected override Task OnInitialize()
			{
				throw new InvalidOperationException("initialize failed");
			}

			public Task Run(CancellationToken ct) => Task.CompletedTask;
		}

		[Test]
		public async Task ResultProcessReturnsValueAndPopsItself()
		{
			using var context = new TestContext();
			await context.Entry<Host>();

			var host = context.Manager.FindState<Host>();
			var result = await host.DoRunResult(21);

			Assert.AreEqual(42, result);
			Assert.IsNull(context.Manager.FindState<ResultProcess>(), "完了後は自動で Pop される");
			CollectionAssert.Contains(host.Popped, "ResultProcess,ProcessEnd");
		}

		[Test]
		public async Task ProcessIsPushedAsChildWhileRunning()
		{
			using var context = new TestContext();
			await context.Entry<Host>();

			var host = context.Manager.FindState<Host>();
			var gate = new TaskCompletionSource<string>();
			var running = host.DoRunGated(gate);

			await context.Tick(2);
			var process = context.Manager.FindState<GatedProcess>();
			Assert.IsNotNull(process, "Run 中は子として積まれている");
			Assert.IsTrue(process.Active);

			gate.SetResult("done");
			Assert.AreEqual("done", await running);
			await context.WaitTransition();
			Assert.IsNull(context.Manager.FindState<GatedProcess>());
		}

		[Test]
		public async Task ExceptionInRunPropagatesAndLeavesManagerUsable()
		{
			using var context = new TestContext();
			await context.Entry<Host>();

			var host = context.Manager.FindState<Host>();

			var error = await TestAsync.Catch(() => host.DoRunThrowing());
			Assert.IsInstanceOf<InvalidOperationException>(error);
			Assert.AreEqual("run failed", error.Message);
			Assert.IsNull(context.Manager.FindState<ThrowingProcess>(), "失敗しても Process は片付く");

			// Run 内の例外は AsyncLock を毒化しないため、続けて遷移できる
			var result = await host.DoRunResult(1);
			Assert.AreEqual(2, result);
		}

		[Test]
		public async Task ExceptionInInitializeHangsAllFurtherTransitions()
		{
			using var context = new TestContext();
			await context.Entry<Host>();

			var host = context.Manager.FindState<Host>();

			var error = await TestAsync.Catch(() => host.DoRunInitThrowing());
			Assert.IsInstanceOf<InvalidOperationException>(error);
			Assert.IsNotNull(context.LastError);

			// Initialize 内の例外は AsyncLock を毒化するため、以後すべて失敗する
			var hang = await TestAsync.Catch(() => host.DoRunResult(1));
			Assert.IsInstanceOf<TransitionHangException>(hang);
			Assert.AreEqual("transition hang : initialize failed", hang.Message);

			// Entry のやり直しでのみ復旧する
			await context.Entry<Host>();
			var recovered = context.Manager.FindState<Host>();
			Assert.AreEqual(4, await recovered.DoRunResult(2));
		}

		[Test]
		public async Task ProcessIsCancelledByExternalToken()
		{
			using var context = new TestContext();
			await context.Entry<Host>();

			var host = context.Manager.FindState<Host>();
			var gate = new TaskCompletionSource<string>();
			using var cts = new CancellationTokenSource();

			var running = host.DoRunGated(gate, cts.Token);
			await context.Tick(2);
			Assert.IsNotNull(context.Manager.FindState<GatedProcess>());

			cts.Cancel();

			var error = await TestAsync.Catch(() => running);
			Assert.IsInstanceOf<OperationCanceledException>(error);
			await context.WaitTransition();
			Assert.IsNull(context.Manager.FindState<GatedProcess>(), "キャンセル後も片付く");
		}

		[Test]
		public async Task ProcessCancelledByTheParentTransitionIsNotReportedAsAnError()
		{
			using var context = new TestContext();
			await context.Entry<Host>();
			var host = context.Manager.FindState<Host>();

			host.DoPushMiddle();
			await context.WaitTransition();
			var middle = context.Manager.FindState<Middle>();

			var gate = new TaskCompletionSource<string>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			middle.DoRunGated(gate);
			await context.WaitTransition();
			Assert.IsNotNull(context.Manager.FindState<GatedProcess>());

			// 親の遷移で Middle ごと畳む。子の Process は先に解体される
			host.DoPushSibling();
			await context.Tick(4);

			Assert.IsNotNull(context.Manager.FindState<Sibling>(), "兄弟に入れ替わる");
			Assert.IsNull(context.Manager.FindState<GatedProcess>(), "子の Process は片付く");
			Assert.IsNull(context.LastError,
				"取り消しは異常ではない: " + context.LastError);

			// 遷移ロックが生きていること
			host.DoPushMiddle();
			await context.WaitTransition();
			Assert.IsNotNull(context.Manager.FindState<Middle>(), "以後の遷移も通る");
		}

		[Test]
		public async Task ProcessExitedRightAfterItsPushIsCancelled()
		{
			using var context = new TestContext();
			await context.Entry<Host>();
			var host = context.Manager.FindState<Host>();

			host.DoPushMiddle();
			await context.WaitTransition();
			var middle = context.Manager.FindState<Middle>();

			var gate = new InitGate();
			middle.DoRunInitGated(gate);
			await context.Tick();

			// 子の初期化中に親の Pop を積む。ロックの受け渡しで Pop が先に子を畳む
			middle.DoPop();
			await context.Tick();
			gate.Initialize.SetResult(true);

			var error = await TestAsync.Catch(() => middle.RunningInitGated);
			Assert.IsInstanceOf<OperationCanceledException>(error, "取り消しとして返る: " + error);
			await context.WaitTransition();
			Assert.IsNull(context.Manager.FindState<Middle>(), "Pop は通る");
			Assert.IsFalse(gate.Ran, "畳まれた Process の Run は呼ばない");
			Assert.IsNull(context.LastError, "取り消しは異常ではない: " + context.LastError);
		}

	}
}
