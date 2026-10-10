using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// 宣言されたブランチ（車線）の検証。
	/// <para>
	/// 主軸は従来どおり排他で、ブランチに入った子は主軸の遷移で畳まれない。
	/// 排他は車線ごとに閉じる。
	/// </para>
	/// </summary>
	public partial class BranchTests
	{
		class OtherBranch : Branch
		{
		}

		class Recorder : IStateObserver
		{
			public List<StateEvent> Events { get; } = new();

			public void OnStateEvent(in StateEvent e) => Events.Add(e);

			public string[] Exits() => Events
				.Where(x => x.Type == StateEventType.Exit)
				.Select(x => $"{x.State?.GetType().Name}:{x.Reason}")
				.ToArray();
		}

		[Push(typeof(MainChild), "Main")]
		[Push(typeof(SideA), "SideA", Branch = typeof(TestBranch))]
		[Push(typeof(SideB), "SideB", Branch = typeof(TestBranch))]
		[Push(typeof(Other), "Other", Branch = typeof(OtherBranch))]
		[Push(typeof(Once), "Once", Branch = typeof(RejectBranch))]
		partial class Root : GameState
		{
			public void DoPushMain() => PushMain<MainChild>();

			public void DoPushSideA() => Handle(async () => await RunSideA<SideA>());

			public void DoPushSideB() => Handle(async () => await RunSideB<SideB>());

			public void DoPushOther() => Handle(async () => await RunOther<Other>());

			/// <summary>完了と例外を検証する用。呼び出し側が待つ。</summary>
			public Task DoRunOnce() => RunOnce<Once>();

			/// <summary>待たずに投げる用。失敗は ErrorHandler に流れる。</summary>
			public void DoPushOnce() => Handle(async () => await RunOnce<Once>());
		}

		class MainChild : GameState
		{
		}

		class SideA : ResidentProcess
		{
		}

		class SideB : ResidentProcess
		{
		}

		class Other : ResidentProcess
		{
		}

		class Once : ResidentProcess
		{
		}

		static string[] TreeNames(TestContext context)
		{
			var list = new List<StateTreeNode>();
			context.Manager.GetTree(list);
			return list.Select(x => x.State.GetType().Name).ToArray();
		}

		[Test]
		public async Task BranchChildIsNotFoldedByMainAxisPush()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushSideA();
			await context.WaitTransition();
			root.DoPushMain();
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<SideA>(), "主軸の Push では畳まれない");
			Assert.IsNotNull(context.Manager.FindState<MainChild>());
		}

		[Test]
		public async Task MainAxisChildIsNotFoldedByBranchPush()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushMain();
			await context.WaitTransition();
			root.DoPushSideA();
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<MainChild>(), "車線が違うので畳まれない");
			Assert.IsNotNull(context.Manager.FindState<SideA>());
		}

		[Test]
		public async Task SameBranchReplacesOccupant()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushSideA();
			await context.WaitTransition();
			root.DoPushSideB();
			await context.WaitTransition();

			// 同じ車線なので排他になる
			Assert.IsNull(context.Manager.FindState<SideA>());
			Assert.IsNotNull(context.Manager.FindState<SideB>());
		}

		[Test]
		public async Task DifferentBranchesCoexist()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushSideA();
			await context.WaitTransition();
			root.DoPushOther();
			await context.WaitTransition();
			root.DoPushMain();
			await context.WaitTransition();

			CollectionAssert.AreEquivalent(
				new[] { "Root", "SideA", "Other", "MainChild" },
				TreeNames(context));
		}

		[Test]
		public async Task ReplacedBranchChildIsNotifiedWithReplace()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();
			var recorder = new Recorder();
			context.Manager.AddObserver(recorder);

			root.DoPushSideA();
			await context.WaitTransition();
			root.DoPushSideB();
			await context.WaitTransition();

			CollectionAssert.Contains(recorder.Exits(), "SideA:Replace");
		}

		[Test]
		public async Task RejectBranchThrowsWhileOccupied()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			var first = root.DoRunOnce();
			await context.WaitTransition();
			var occupant = context.Manager.FindState<Once>();
			Assert.IsNotNull(occupant);

			var error = await TestAsync.Catch(() => root.DoRunOnce());

			Assert.IsInstanceOf<BranchRejectedException>(error);
			Assert.AreEqual(typeof(RejectBranch), ((BranchRejectedException)error).Branch);
			Assert.IsFalse(first.IsCompleted, "拒否しても占有中のものは影響を受けない");
			Assert.AreSame(occupant, context.Manager.FindState<Once>());
		}

		[Test]
		public async Task RejectBranchDoesNotStopFollowingTransitions()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushOnce();
			await context.WaitTransition();
			await TestAsync.Catch(() => root.DoRunOnce());

			// 拒否は想定内の失敗なので、遷移ロックを毒化しない
			root.DoPushMain();
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<MainChild>());
			Assert.IsNull(context.LastError, "ErrorHandler にも流れない");
		}

		[Test]
		public async Task BranchIsFreedWhenProcessCompletes()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			var running = root.DoRunOnce();
			await context.WaitTransition();
			context.Manager.FindState<Once>().Finish();
			await running;
			await context.WaitTransition();

			Assert.IsNull(context.Manager.FindState<Once>(), "Run の完了で自動的に畳まれる");

			// 空いたので再度入れられる
			root.DoPushOnce();
			await context.WaitTransition();
			Assert.IsNotNull(context.Manager.FindState<Once>());
		}

		[Test]
		public async Task BranchAppearsInTree()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushMain();
			await context.WaitTransition();
			root.DoPushSideA();
			await context.WaitTransition();

			var list = new List<StateTreeNode>();
			context.Manager.GetTree(list);

			Assert.IsNull(list.First(x => x.State is MainChild).Branch, "主軸は null");
			Assert.AreEqual(typeof(TestBranch), list.First(x => x.State is SideA).Branch);
			StringAssert.Contains("[TestBranch] SideA", context.Manager.DumpTree());
		}

		[Test]
		public async Task ParentExitFoldsBranchChildren()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushSideA();
			await context.WaitTransition();
			root.DoPushOther();
			await context.WaitTransition();

			await context.Entry<Root>();

			Assert.IsNull(context.Manager.FindState<SideA>());
			Assert.IsNull(context.Manager.FindState<Other>());
		}
	}
}
