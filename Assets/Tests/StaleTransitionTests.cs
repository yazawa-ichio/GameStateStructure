using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// <c>Entry</c> より前に始まった処理が、復旧後のマネージャに触らないことの検証。
	/// <para>
	/// 遷移はどの await でも中断され得る。再開した先が「復旧後の木」「差し替え後の
	/// Decorator」「新しい AsyncLock」であることを、境界ごとに固定する。
	/// </para>
	/// </summary>
	public partial class StaleTransitionTests
	{
		/// <summary>どのステートを扱ったかを記録するだけの Decorator。</summary>
		class Recorder : IDecorator
		{
			public List<string> PostExit { get; } = new List<string>();

			public List<string> PostInitialize { get; } = new List<string>();

			public int Priority => 0;

			public Task DoPreInitialize(GameState state) => Task.CompletedTask;

			public Task DoPostInitialize(GameState state)
			{
				PostInitialize.Add(state.GetType().Name);
				return Task.CompletedTask;
			}

			public Task DoPreEnter(GameState state) => Task.CompletedTask;

			public void DoPostEnter(GameState state) { }

			public Task DoPreExit(GameState state) => Task.CompletedTask;

			public Task DoPostExit(GameState state)
			{
				PostExit.Add(state.GetType().Name);
				return Task.CompletedTask;
			}

			public void ForceRelease() { }
		}

		/// <summary>
		/// <see cref="ForceRelease"/> が <see cref="Run"/> を<b>同期で</b>完了させる終了処理。
		/// 継続が Entry の解体処理の途中に割り込む。
		/// </summary>
		class SyncClosingExitTask : IExitTask
		{
			readonly TaskCompletionSource<bool> m_Source = new TaskCompletionSource<bool>();

			public Task Run(CancellationToken ct) => m_Source.Task;

			public void ForceRelease() => m_Source.TrySetResult(true);
		}

		/// <summary>継続を後回しにする終了処理。再開は Entry の完了後になる。</summary>
		class AsyncClosingExitTask : IExitTask
		{
			readonly TaskCompletionSource<bool> m_Source =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public Task Run(CancellationToken ct) => m_Source.Task;

			public void ForceRelease() => m_Source.TrySetResult(true);
		}

		/// <summary>受け取った例外を全部ためる。二重報告も見える。</summary>
		class ErrorRecorder : IErrorHandler
		{
			public List<Exception> Errors { get; } = new List<Exception>();

			public void Handle(Exception ex) => Errors.Add(ex);
		}

		/// <summary>await のたびに同じ例外インスタンスを送出する。</summary>
		static readonly Task s_SharedFailure =
			Task.FromException(new InvalidOperationException("reused failure"));

		static TaskCompletionSource<bool> s_Gate;

		[SetUp]
		public void SetUp() => s_Gate = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		[Push(typeof(Child), "Child")]
		[Push(typeof(SlowEnter), "SlowEnter")]
		[Push(typeof(SlowInit), "SlowInit")]
		[Push(typeof(SlowProcess), "SlowProcess")]
		[Push(typeof(ReusedFailure), "ReusedFailure")]
		partial class Root : GameState
		{
			/// <summary>Handle を通さず呼び出し側が待つ。</summary>
			public Task DoAwaitReusedFailure() => RunReusedFailure<ReusedFailure>();

			public int PopChildCount { get; private set; }

			public void DoPushChild() => PushChild<Child>();

			public void DoPushSlowEnter() => PushSlowEnter<SlowEnter>();

			public void DoPushSlowInit() => PushSlowInit<SlowInit>();

			public void DoRunSlowProcess() => Handle(async () => await RunSlowProcess<SlowProcess>());

			protected override void OnPopChild(GameState child, PopReason reason)
			{
				PopChildCount++;
			}
		}

		class Child : GameState
		{
			public void DoPop() => Pop();
		}

		/// <summary>OnPreEnter で止まる。EnterState の途中で中断される。</summary>
		class SlowEnter : GameState
		{
			// Enter 前は Active でないため FindState では拾えない
			public static SlowEnter Instance;

			public static int EnterCount;

			protected override Task OnInitialize()
			{
				Instance = this;
				return Task.CompletedTask;
			}

			protected override Task OnPreEnter() => s_Gate.Task;

			protected override void OnEnter() => EnterCount++;
		}

		/// <summary>OnInitialize で止まる。木に繋がる前に中断される。</summary>
		class SlowInit : GameState
		{
			protected override Task OnInitialize() => s_Gate.Task;
		}

		/// <summary>
		/// 失敗した <see cref="Task"/> を使い回す IProcess。
		/// await のたびに<b>同じ例外インスタンス</b>が送出される。
		/// </summary>
		class ReusedFailure : GameState, IProcess
		{
			protected override Task OnInitialize() => s_SharedFailure;

			public Task Run(CancellationToken ct) => Task.CompletedTask;
		}

		/// <summary>OnInitialize で止まる IProcess。</summary>
		class SlowProcess : GameState, IProcess
		{
			protected override Task OnInitialize() => s_Gate.Task;

			public Task Run(CancellationToken ct) => Task.CompletedTask;
		}

		static async Task<TestContext> EntryWith(Recorder recorder)
		{
			var context = new TestContext();
			var config = context.CreateConfig();
			config.Decorator.Register(recorder);
			await context.Manager.Entry<Root>(new ParameterHolder(), config);
			return context;
		}

		static async Task<TestContext> EntryWith(IErrorHandler handler)
		{
			var context = new TestContext();
			await context.Manager.Entry<Root>(
				new ParameterHolder(), new Config { ErrorHandler = handler });
			return context;
		}

		static Task ReEntry(TestContext context, IErrorHandler handler)
		{
			return context.Manager.Entry<Root>(
				new ParameterHolder(), new Config { ErrorHandler = handler });
		}

		static Task ReEntry(TestContext context, Recorder recorder)
		{
			var config = context.CreateConfig();
			config.Decorator.Register(recorder);
			return context.Manager.Entry<Root>(new ParameterHolder(), config);
		}

		// 強制解放が Run を同期完了させると、世代を進める前に解体が再開する
		[Test]
		public async Task SyncResumedExitDoesNotRunTheNormalTail()
		{
			var recorder = new Recorder();
			using var context = await EntryWith(recorder);
			var root = context.Manager.FindState<Root>();
			root.DoPushChild();
			await context.WaitTransition();

			var child = context.Manager.FindState<Child>();
			child.Context.RegisterExitTask(new SyncClosingExitTask());
			child.DoPop();
			await context.Tick(2);
			Assert.AreEqual(0, root.PopChildCount, "まだ解体の途中");

			await ReEntry(context, new Recorder());
			await context.Tick(4);

			Assert.AreEqual(0, root.PopChildCount,
				"打ち切られた解体は OnPopChild まで進まない");
			CollectionAssert.DoesNotContain(recorder.PostExit, "Child",
				"打ち切られた解体は PostExit まで進まない");
		}

		// 世代確認が末尾にあると、差し替え後の Decorator に旧ステートが渡る
		[Test]
		public async Task RecoveredDecoratorDoesNotSeeTheStoppedState()
		{
			using var context = await EntryWith(new Recorder());
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();

			var child = context.Manager.FindState<Child>();
			child.Context.RegisterExitTask(new AsyncClosingExitTask());
			child.DoPop();
			await context.Tick(2);

			var recovered = new Recorder();
			await ReEntry(context, recovered);
			await context.Tick(4);

			CollectionAssert.DoesNotContain(recovered.PostExit, "Child",
				"復旧後の Decorator に旧ステートの PostExit が渡らない");
		}

		// EnterState の途中で中断されると、解放済みステートが Active になる
		[Test]
		public async Task StoppedEnterDoesNotActivateTheReleasedState()
		{
			SlowEnter.EnterCount = 0;
			SlowEnter.Instance = null;
			using var context = await EntryWith(new Recorder());
			context.Manager.FindState<Root>().DoPushSlowEnter();
			await context.Tick(3);

			var slow = SlowEnter.Instance;
			Assert.IsNotNull(slow, "OnPreEnter まで到達している");
			Assert.IsFalse(slow.Active, "まだ Enter していない");

			await ReEntry(context, new Recorder());
			s_Gate.TrySetResult(true);
			await context.Tick(4);

			Assert.AreEqual(0, SlowEnter.EnterCount, "打ち切られた遷移先は Enter されない");
			Assert.IsFalse(slow.Active, "解放済みのステートが Active に戻らない");
			Assert.IsNull(context.Manager.FindState<SlowEnter>(), "復旧後の木に現れない");
		}

		// 初期化中に中断されると、差し替え後の Decorator に未接続の旧ステートが渡る
		[Test]
		public async Task RecoveredDecoratorDoesNotSeeTheStoppedInitialization()
		{
			using var context = await EntryWith(new Recorder());
			context.Manager.FindState<Root>().DoPushSlowInit();
			await context.Tick(3);

			var recovered = new Recorder();
			await ReEntry(context, recovered);
			s_Gate.TrySetResult(true);
			await context.Tick(4);

			CollectionAssert.DoesNotContain(recovered.PostInitialize, "SlowInit",
				"復旧後の Decorator に未接続の旧ステートが渡らない");
		}

		// 打ち切られた Process の通常例外が、復旧後の ErrorHandler に届く
		[Test]
		public async Task StaleProcessFailureIsNotReportedToTheRecoveredHandler()
		{
			var oldHandler = new ErrorRecorder();
			using var context = await EntryWith(oldHandler);
			context.Manager.FindState<Root>().DoRunSlowProcess();
			await context.Tick(3);

			var newHandler = new ErrorRecorder();
			await ReEntry(context, newHandler);
			s_Gate.TrySetException(new InvalidOperationException("initialize failed"));
			await context.Tick(4);

			CollectionAssert.IsEmpty(newHandler.Errors,
				"打ち切られた処理の例外は復旧後の ErrorHandler に流れない");
		}

		// GoTo / Push の初期化が Entry 後に通常例外で終わる場合も同じ
		[Test]
		public async Task StalePushFailureIsNotReportedToTheRecoveredHandler()
		{
			var oldHandler = new ErrorRecorder();
			using var context = await EntryWith(oldHandler);
			context.Manager.FindState<Root>().DoPushSlowInit();
			await context.Tick(3);

			var newHandler = new ErrorRecorder();
			await ReEntry(context, newHandler);
			s_Gate.TrySetException(new InvalidOperationException("initialize failed"));
			await context.Tick(4);

			CollectionAssert.IsEmpty(newHandler.Errors,
				"打ち切られた Push の例外は復旧後の ErrorHandler に流れない");
		}

		/// <summary>
		/// 組み込み遷移の境界と、預かった非同期処理の境界は別の事象を報告する。
		/// 同じ例外が両方を通ることはあるが、1 件へ畳む契約は無い。
		/// </summary>
		[Test]
		public async Task ProcessFailureIsReportedByBothBoundaries()
		{
			var handler = new ErrorRecorder();
			using var context = await EntryWith(handler);
			context.Manager.FindState<Root>().DoRunSlowProcess();
			await context.Tick(3);

			var failure = new InvalidOperationException("initialize failed");
			s_Gate.TrySetException(failure);
			await context.Tick(4);

			Assert.AreEqual(2, handler.Errors.Count(x => ReferenceEquals(x, failure)),
				"組み込み遷移の失敗と、預かった非同期処理の失敗の 2 件: "
					+ string.Join(" / ", handler.Errors.Select(x => x.GetType().Name)));
		}

		/// <summary>
		/// 直接 await した場合は組み込み遷移の境界だけ。
		/// ここが ProcessPush に報告を残している理由。
		/// </summary>
		[Test]
		public async Task DirectlyAwaitedProcessFailureIsReportedByTheProcessBoundary()
		{
			var handler = new ErrorRecorder();
			using var context = await EntryWith(handler);

			var error = await TestAsync.Catch(
				() => context.Manager.FindState<Root>().DoAwaitReusedFailure());

			Assert.IsInstanceOf<InvalidOperationException>(error, "呼び出し元にも例外が届く");
			Assert.AreEqual(1, handler.Errors.Count(x => ReferenceEquals(x, error)),
				"預かった非同期処理の境界を通らないため 1 件");
		}

		// 打ち切られた Process の通常例外が、復旧後のロックを毒化する
		[Test]
		public async Task StaleProcessFailureDoesNotPoisonTheRecoveredLock()
		{
			using var context = await EntryWith(new Recorder());
			context.Manager.FindState<Root>().DoRunSlowProcess();
			await context.Tick(3);

			await ReEntry(context, new Recorder());
			// 打ち切られた初期化を通常の例外で失敗させる
			s_Gate.TrySetException(new InvalidOperationException("initialize failed"));
			await context.Tick(4);

			// 復旧後のマネージャは動き続ける
			context.Manager.FindState<Root>().DoPushChild();
			await context.Tick(4);

			Assert.IsNotNull(context.Manager.FindState<Child>(),
				"復旧後のロックが毒化されていない: " + context.LastError);
		}
	}
}
