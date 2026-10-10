using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// ステート探索 API の検証。
	/// </summary>
	public partial class QueryTests
	{
		interface IMarker
		{
			string Name { get; }
		}

		[Push(typeof(Mid), "Mid")]
		partial class Root : GameState, IMarker
		{
			public string Name => "Root";

			public void DoPushMid() => PushMid<Mid>();
		}

		[Push(typeof(Leaf), "Leaf")]
		partial class Mid : GameState, IMarker
		{
			public string Name => "Mid";

			public void DoPushLeaf() => PushLeaf<Leaf>();
		}

		class Leaf : GameState
		{
			public GameState ParentState => GetParentState();

			public T Nearest<T>() where T : class => FindParentState<T>();

			public IEnumerable<T> Ancestors<T>() where T : class => FindParentStates<T>();
		}

		async Task BuildTree(TestContext context)
		{
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushMid();
			await context.WaitTransition();
			context.Manager.FindState<Mid>().DoPushLeaf();
			await context.WaitTransition();
		}

		[Test]
		public async Task FindParentStateReturnsNearestAncestor()
		{
			using var context = new TestContext();
			await BuildTree(context);

			var leaf = context.Manager.FindState<Leaf>();

			Assert.AreSame(context.Manager.FindState<Mid>(), leaf.ParentState);
			Assert.AreSame(context.Manager.FindState<Mid>(), leaf.Nearest<IMarker>());
		}

		[Test]
		public async Task FindParentStatesReturnsAncestorsFromNearest()
		{
			using var context = new TestContext();
			await BuildTree(context);

			var leaf = context.Manager.FindState<Leaf>();
			var names = leaf.Ancestors<IMarker>().Select(x => x.Name).ToArray();

			CollectionAssert.AreEqual(new[] { "Mid", "Root" }, names);
		}

		[Test]
		public async Task IsRootAndIsLeafReflectPositionInTree()
		{
			using var context = new TestContext();
			await BuildTree(context);

			var root = context.Manager.FindState<Root>();
			var mid = context.Manager.FindState<Mid>();
			var leaf = context.Manager.FindState<Leaf>();

			Assert.IsTrue(root.IsRoot);
			Assert.IsFalse(root.IsLeaf);

			Assert.IsFalse(mid.IsRoot);
			Assert.IsFalse(mid.IsLeaf);

			Assert.IsFalse(leaf.IsRoot);
			Assert.IsTrue(leaf.IsLeaf);
		}

		[Test]
		public async Task FindAllStatesMatchesByInterface()
		{
			using var context = new TestContext();
			await BuildTree(context);

			var names = context.Manager.FindAllStates<IMarker>().Select(x => x.Name).ToArray();

			CollectionAssert.AreEquivalent(new[] { "Root", "Mid" }, names);
		}

		[Test]
		public async Task AllCollectionSpansMultipleManagers()
		{
			using var first = new TestContext();
			using var second = new TestContext();

			await BuildTree(first);
			await BuildTree(second);

			// IMarker はこのテストクラス内でしか使っていないため、
			// 他のテストのステートが混ざることはない
			var names = GameStateManager.All.GetAllStates<IMarker>().Select(x => x.Name).ToArray();

			Assert.AreEqual(4, names.Length, "2 つの Manager から Root と Mid が取れる");
			Assert.AreEqual(2, names.Count(x => x == "Root"));
			Assert.AreEqual(2, names.Count(x => x == "Mid"));
		}
	}
}
