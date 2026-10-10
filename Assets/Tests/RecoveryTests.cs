using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// 解体が失敗・停止したときの回収と、Entry による復旧の検証。
	/// <para>
	/// 解体は木から切り離してから進むため、途中で止まると通常の解放経路からは
	/// 到達できなくなる。取り残しが出ないことと、打ち切った遷移が
	/// 復旧後のマネージャに触らないことを固定する。
	/// </para>
	/// </summary>
	public partial class RecoveryTests
	{
		class Probe : IDisposable
		{
			public int DisposeCount { get; private set; }

			public void Dispose() => DisposeCount++;
		}

		/// <summary>
		/// <see cref="ForceRelease"/> で <see cref="Run"/> を完了させる終了処理。
		/// 「待たずに片付ける」という契約どおりの実装で、
		/// 強制解放の後に解体の続きが動き出す。
		/// </summary>
		class ClosingExitTask : IExitTask
		{
			readonly TaskCompletionSource<bool> m_Source =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public Task Run(CancellationToken ct) => m_Source.Task;

			public void ForceRelease() => m_Source.TrySetResult(true);
		}

		/// <summary>完了しない終了処理。解体が止まる状況を作る。</summary>
		class HangingExitTask : IExitTask
		{
			readonly TaskCompletionSource<bool> m_Source =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public Task Run(CancellationToken ct) => m_Source.Task;

			public void ForceRelease() { }

			public void Complete() => m_Source.TrySetResult(true);
		}

		/// <summary>指定したステートの PreExit だけ失敗させる。</summary>
		class FailingPreExitDecorator : IDecorator
		{
			public GameState Target { get; set; }

			public int Priority => 0;

			public Task DoPreInitialize(GameState state) => Task.CompletedTask;

			public Task DoPostInitialize(GameState state) => Task.CompletedTask;

			public Task DoPreEnter(GameState state) => Task.CompletedTask;

			public void DoPostEnter(GameState state) { }

			public Task DoPreExit(GameState state)
			{
				if (state == Target)
				{
					throw new InvalidOperationException("pre exit failed");
				}
				return Task.CompletedTask;
			}

			public Task DoPostExit(GameState state) => Task.CompletedTask;

			public void ForceRelease() { }
		}

		[Push(typeof(Middle), "Middle")]
		[GoTo(typeof(Other), "Other")]
		partial class Root : GameState
		{
			public void DoPushMiddle() => PushMiddle<Middle>();

			public void DoGoToOther() => GoToOther<Other>();
		}

		[Push(typeof(Leaf), "Leaf")]
		[Push(typeof(Side), "Side", Branch = typeof(TestBranch))]
		partial class Middle : GameState
		{
			public int ExitCount { get; private set; }

			protected override Task OnExit()
			{
				ExitCount++;
				return Task.CompletedTask;
			}

			public void DoPushLeaf() => PushLeaf<Leaf>();

			public void DoRunSide() => Handle(async () => await RunSide<Side>());

			public void DoPop() => Pop();
		}

		class Leaf : GameState
		{
		}

		/// <summary>車線を占有し続ける子。主軸の Leaf と同時に存在する。</summary>
		class Side : ResidentProcess
		{
		}

		class Other : GameState
		{
		}

		[Test]
		public async Task DescendantsAreReclaimedWhenTheParentFailsToPreExit()
		{
			using var context = new TestContext();
			var decorator = new FailingPreExitDecorator();
			var config = context.CreateConfig();
			config.Decorator.Register(decorator);
			await context.Manager.Entry<Root>(new ParameterHolder(), config);

			context.Manager.FindState<Root>().DoPushMiddle();
			await context.WaitTransition();
			var middle = context.Manager.FindState<Middle>();
			middle.DoPushLeaf();
			await context.WaitTransition();

			var middleProbe = new Probe();
			var leafProbe = new Probe();
			middle.Context.Manage(middleProbe);
			context.Manager.FindState<Leaf>().Context.Manage(leafProbe);

			// Decorators.DoPreExit は子を辿る前に呼ばれる。
			// ここで失敗すると Middle は木から外れた後で、子は未処理のまま残る
			decorator.Target = middle;
			middle.DoPop();
			await context.Tick(4);

			Assert.IsNotNull(context.LastError);
			Assert.AreEqual(1, middleProbe.DisposeCount, "失敗したステート自身は回収される");
			Assert.AreEqual(1, leafProbe.DisposeCount, "その子孫も回収される");
		}

		[Test]
		public async Task OnExitIsSkippedWhenTheStoppedExitIsForceReleased()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushMiddle();
			await context.WaitTransition();
			var middle = context.Manager.FindState<Middle>();

			// 通常の終了なら OnExit は呼ばれる
			middle.DoPop();
			await context.WaitTransition();
			Assert.AreEqual(1, middle.ExitCount, "通常の終了では呼ばれる");

			context.Manager.FindState<Root>().DoPushMiddle();
			await context.WaitTransition();
			var stopped = context.Manager.FindState<Middle>();
			stopped.Context.RegisterExitTask(new ClosingExitTask());

			// Context.DisposeAsync が IExitTask の完了待ちで止まる
			stopped.DoPop();
			await context.Tick(2);
			Assert.AreEqual(0, stopped.ExitCount, "まだ解体の途中");

			// Entry の復旧が IExitTask を ForceRelease し、
			// 止まっていた Run が完了して解体の続きが動き出す
			await context.Entry<Root>();
			await context.Tick(4);

			Assert.AreEqual(0, stopped.ExitCount,
				"OnExit は通常の終了時のみ。強制解放された後には呼ばれない");
			Assert.IsNull(context.LastError);
		}

		[Test]
		public async Task PendingSiblingsAreReclaimedWhenAnExitHangs()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().DoPushMiddle();
			await context.WaitTransition();
			var middle = context.Manager.FindState<Middle>();

			// 主軸と車線で子が 2 つ並ぶ。解体は宣言順に処理される
			middle.DoPushLeaf();
			await context.WaitTransition();
			middle.DoRunSide();
			await context.WaitTransition();

			var leaf = context.Manager.FindState<Leaf>();
			var side = context.Manager.FindState<Side>();
			Assert.IsNotNull(leaf);
			Assert.IsNotNull(side);

			var middleProbe = new Probe();
			var leafProbe = new Probe();
			var sideProbe = new Probe();
			middle.Context.Manage(middleProbe);
			leaf.Context.Manage(leafProbe);
			side.Context.Manage(sideProbe);

			// 先頭の子の解体で止まる。Side は未処理のまま Children に残り、
			// Middle は既に木から外れているため誰も辿れない
			leaf.Context.RegisterExitTask(new HangingExitTask());
			middle.DoPop();
			await context.Tick(3);
			Assert.AreEqual(0, sideProbe.DisposeCount, "まだ解体されていない");

			await context.Entry<Root>();

			Assert.AreEqual(1, middleProbe.DisposeCount, "止まった親は回収される");
			Assert.AreEqual(1, leafProbe.DisposeCount, "止まっている子も回収される");
			Assert.AreEqual(1, sideProbe.DisposeCount, "未処理のまま残った兄弟も回収される");
		}

		[Test]
		public async Task StaleGoToDoesNotReplaceTheRecoveredRoot()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var oldRoot = context.Manager.FindState<Root>();
			var hang = new HangingExitTask();
			oldRoot.Context.RegisterExitTask(hang);

			// 旧ルートの解体で止まる
			oldRoot.DoGoToOther();
			await context.Tick(2);
			Assert.IsNull(context.Manager.FindState<Other>(), "まだ入れ替わっていない");

			// 止まったまま Entry で復旧する
			await context.Entry<Root>();
			var recovered = context.Manager.Root;
			Assert.IsInstanceOf<Root>(recovered);

			// 止まっていた解体が後から完了する
			hang.Complete();
			await context.Tick(4);

			Assert.AreSame(recovered, context.Manager.Root,
				"打ち切られた GoTo が復旧後のルートを上書きしない");
			Assert.IsNull(context.Manager.FindState<Other>(),
				"打ち切られた遷移先は Enter されない");
		}
	}
}
