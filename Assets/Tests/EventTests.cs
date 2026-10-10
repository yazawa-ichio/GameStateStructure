using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// SubscribeEvent / PublishEvent の配送範囲と完了待ちを検証する。
	/// </summary>
	public partial class EventTests
	{
		[SubscribeEvent]
		interface ISinglePing
		{
			void SinglePing(int value);
		}

		[SubscribeEvent(Broadcast = true)]
		interface IBroadcastPing
		{
			void BroadcastPing(int value);
		}

		[SubscribeEvent(Broadcast = true, ChildOnly = false)]
		interface IGlobalPing
		{
			void GlobalPing(int value);
		}

		[Push(typeof(Mid), "Mid")]
		partial class Root : GameState, ISinglePing, IBroadcastPing
		{
			public List<string> Received { get; } = new();

			void ISinglePing.SinglePing(int value) => Received.Add($"Single({value})");

			void IBroadcastPing.BroadcastPing(int value) => Received.Add($"Broadcast({value})");

			public void DoPushMid() => PushMid<Mid>();
		}

		[Push(typeof(Leaf), "Leaf")]
		partial class Mid : GameState, ISinglePing, IBroadcastPing
		{
			public List<string> Received { get; } = new();

			public bool AsyncDone { get; private set; }

			void ISinglePing.SinglePing(int value) => Received.Add($"Single({value})");

			void IBroadcastPing.BroadcastPing(int value) => Received.Add($"Broadcast({value})");

			[SubscribeEvent]
			public async Task Work()
			{
				await Task.Delay(50);
				AsyncDone = true;
			}

			public void DoPushLeaf() => PushLeaf<Leaf>();
		}

		[PublishEvent(typeof(ISinglePing))]
		[PublishEvent(typeof(IBroadcastPing))]
		[PublishEvent(typeof(Mid))]
		partial class Leaf : GameState
		{
			public void DoSingle(int value) => SinglePing(value);

			public void DoBroadcast(int value) => BroadcastPing(value);

			public Task DoWork() => Work();

			public void DoPop() => Pop();
		}

		[Push(typeof(Listener), "Listener")]
		[Push(typeof(Publisher), "Publisher", Branch = typeof(TestBranch))]
		partial class ParallelRoot : GameState
		{
			public void DoPushListener() => PushListener<Listener>();

			public void DoPushPublisher() => Handle(async () => await RunPublisher<Publisher>());
		}

		class Listener : GameState, IGlobalPing
		{
			public List<string> Received { get; } = new();

			void IGlobalPing.GlobalPing(int value) => Received.Add($"Global({value})");
		}

		[PublishEvent(typeof(IGlobalPing))]
		partial class Publisher : ResidentProcess
		{
			public void DoGlobal(int value) => GlobalPing(value);
		}

		async Task<TestContext> EntryThreeLevels()
		{
			var context = new TestContext();
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushMid();
			await context.WaitTransition();
			context.Manager.FindState<Mid>().DoPushLeaf();
			await context.WaitTransition();
			return context;
		}

		[Test]
		public async Task SingleDispatchReachesNearestAncestorOnly()
		{
			using var context = await EntryThreeLevels();

			context.Manager.FindState<Leaf>().DoSingle(1);

			CollectionAssert.AreEqual(new[] { "Single(1)" }, context.Manager.FindState<Mid>().Received);
			Assert.IsEmpty(context.Manager.FindState<Root>().Received, "最も近い祖先で止まる");
		}

		[Test]
		public async Task BroadcastDispatchReachesAllAncestors()
		{
			using var context = await EntryThreeLevels();

			context.Manager.FindState<Leaf>().DoBroadcast(2);

			CollectionAssert.AreEqual(new[] { "Broadcast(2)" }, context.Manager.FindState<Mid>().Received);
			CollectionAssert.AreEqual(new[] { "Broadcast(2)" }, context.Manager.FindState<Root>().Received);
		}

		[Test]
		public async Task AsyncEventCanBeAwaitedToCompletion()
		{
			using var context = await EntryThreeLevels();

			var mid = context.Manager.FindState<Mid>();
			Assert.IsFalse(mid.AsyncDone);

			await context.Manager.FindState<Leaf>().DoWork();

			Assert.IsTrue(mid.AsyncDone, "イベントの完了を待てる");
		}

		[Test]
		public async Task InactivePublisherDoesNotDispatch()
		{
			using var context = await EntryThreeLevels();

			var leaf = context.Manager.FindState<Leaf>();
			var mid = context.Manager.FindState<Mid>();

			leaf.DoPop();
			await context.WaitTransition();
			Assert.IsFalse(leaf.Active);

			leaf.DoSingle(99);

			Assert.IsEmpty(mid.Received, "非 Active なステートからは配送されない");
		}

		[Test]
		public async Task GlobalDispatchReachesNonAncestors()
		{
			using var context = new TestContext();
			await context.Entry<ParallelRoot>();

			var root = context.Manager.FindState<ParallelRoot>();
			root.DoPushListener();
			await context.WaitTransition();
			root.DoPushPublisher();
			await context.WaitTransition();

			var listener = context.Manager.FindState<Listener>();
			Assert.IsNotNull(listener, "別の車線に入ったため共存している");

			context.Manager.FindState<Publisher>().DoGlobal(3);

			// Listener は祖先ではなく兄弟。ChildOnly = false で初めて届く
			CollectionAssert.AreEqual(new[] { "Global(3)" }, listener.Received);
		}
	}
}
