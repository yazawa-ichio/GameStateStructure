using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// 観測用 API の検証。
	/// FindAllStates は結果がフラットになるため、木の形は GetTree で見る。
	/// </summary>
	public partial class ObservationTests
	{
		[Push(typeof(Mid), "Mid")]
		[Push(typeof(Side), "Side", Branch = typeof(TestBranch))]
		partial class Root : GameState
		{
			public void DoPushMid() => PushMid<Mid>();

			public void DoPushSide() => Handle(async () => await RunSide<Side>());
		}

		[Push(typeof(Leaf), "Leaf")]
		partial class Mid : GameState
		{
			public void DoPushLeaf() => PushLeaf<Leaf>();
		}

		class Leaf : GameState
		{
		}

		class Side : ResidentProcess
		{
		}

		async Task BuildThreeLevels(TestContext context)
		{
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushMid();
			await context.WaitTransition();
			context.Manager.FindState<Mid>().DoPushLeaf();
			await context.WaitTransition();
		}

		[Test]
		public async Task TreeIsEnumeratedParentFirstWithDepth()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			var list = new List<StateTreeNode>();
			context.Manager.GetTree(list);

			CollectionAssert.AreEqual(
				new[] { "Root", "Mid", "Leaf" },
				list.Select(x => x.State.GetType().Name).ToArray());
			CollectionAssert.AreEqual(new[] { 0, 1, 2 }, list.Select(x => x.Depth).ToArray());
		}

		[Test]
		public async Task BranchChildrenAppearAsSiblings()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushMid();
			await context.WaitTransition();
			root.DoPushSide();
			await context.WaitTransition();

			var list = new List<StateTreeNode>();
			context.Manager.GetTree(list);

			CollectionAssert.AreEqual(new[] { 0, 1, 1 }, list.Select(x => x.Depth).ToArray());
		}

		[Test]
		public async Task GetChildrenReturnsDirectChildrenOnly()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			var children = new List<GameState>();
			context.Manager.GetChildren(context.Manager.FindState<Root>(), children);

			// 孫の Leaf は含まない
			CollectionAssert.AreEqual(
				new[] { "Mid" },
				children.Select(x => x.GetType().Name).ToArray());
		}

		[Test]
		public async Task GetParentReturnsOwner()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			var mid = context.Manager.FindState<Mid>();
			var leaf = context.Manager.FindState<Leaf>();

			Assert.AreSame(mid, context.Manager.GetParent(leaf));
			Assert.AreSame(context.Manager.FindState<Root>(), context.Manager.GetParent(mid));
		}

		[Test]
		public async Task GetParentOfRootIsNull()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			Assert.IsNull(context.Manager.GetParent(context.Manager.FindState<Root>()));
		}

		[Test]
		public async Task DumpTreeShowsHierarchy()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			var dump = context.Manager.DumpTree();

			// 1 行目はヘッダ、2 行目以降がツリー
			var lines = dump.Split('\n').Select(x => x.TrimEnd('\r')).ToArray();
			StringAssert.StartsWith("[idle]", lines[0]);
			Assert.AreEqual("Root", lines[1]);
			Assert.AreEqual("  Mid", lines[2]);
			Assert.AreEqual("    Leaf", lines[3]);
		}

		[Test]
		public async Task AllExposesLiveManagers()
		{
			using var first = new TestContext();
			using var second = new TestContext();
			await first.Entry<Root>();
			await second.Entry<Root>();

			var managers = new List<GameStateManager>();
			GameStateManager.All.GetManagers(managers);

			// 観測ツールが Manager 一覧を取れる
			CollectionAssert.Contains(managers, first.Manager);
			CollectionAssert.Contains(managers, second.Manager);
			Assert.AreEqual(GameStateManager.All.Count, managers.Count);
		}

		[Test]
		public void DumpTreeBeforeEntryReportsNoState()
		{
			using var context = new TestContext();

			StringAssert.Contains("(no state)", context.Manager.DumpTree());
		}
	}
}
