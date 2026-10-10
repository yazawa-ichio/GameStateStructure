using GameStateStructure.Diagnostics;
using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// SubscribeEvent を Editor から発火するための列挙の検証。
	/// </summary>
	public partial class EventCatalogTests
	{
		enum Mode
		{
			Normal,
			Hard,
		}

		// 実プロジェクトの IReboot と同じ形。インターフェースに付ける
		[SubscribeEvent]
		interface IReboot
		{
			void Reboot();
		}

		[Push(typeof(Child), "Child")]
		partial class Root : GameState, IReboot
		{
			public string Received { get; private set; }

			public bool Rebooted { get; private set; }

			// 明示的実装なので private になる
			void IReboot.Reboot() => Rebooted = true;

			[SubscribeEvent]
			public void Ping(int value) => Received = $"Ping({value})";

			[SubscribeEvent(Internal = true, Transition = typeof(Child))]
			public void DebugGoChild() => PushChild<Child>();

			[SubscribeEvent]
			public void WithEnum(Mode mode, bool flag) => Received = $"{mode},{flag}";

			[SubscribeEvent]
			public void Unsupported(GameState state) => Received = "unsupported";
		}

		class Child : GameState
		{
		}

		static EventEntry Find(GameState state, string label)
			=> EventCatalog.Get(state).First(x => x.Label == label);

		[Test]
		public async Task EventsAreDiscoveredIncludingExplicitInterfaceImplementations()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var labels = EventCatalog.Get(context.Manager.FindState<Root>()).Select(x => x.Label).ToArray();

			CollectionAssert.AreEquivalent(
				new[] { "Ping", "DebugGoChild", "WithEnum", "Unsupported", "IReboot.Reboot" },
				labels);
		}

		[Test]
		public async Task ExplicitInterfaceImplementationCanBeInvoked()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var root = context.Manager.FindState<Root>();
			EventCatalog.Invoke(root, Find(root, "IReboot.Reboot"), null);

			Assert.IsTrue(root.Rebooted, "private な明示的実装でも呼べる");
		}

		[Test]
		public async Task ArgumentsArePassed()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var root = context.Manager.FindState<Root>();
			EventCatalog.Invoke(root, Find(root, "WithEnum"), new object[] { Mode.Hard, true });

			Assert.AreEqual("Hard,True", root.Received);
		}

		[Test]
		public async Task UnsupportedParameterIsMarkedAsNotInvocable()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var root = context.Manager.FindState<Root>();

			Assert.IsTrue(Find(root, "WithEnum").CanInvoke);
			Assert.IsFalse(Find(root, "Unsupported").CanInvoke, "GameState は値を用意できない");
		}

		[Test]
		public async Task InternalAndTransitionMetadataAreCarried()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var entry = Find(context.Manager.FindState<Root>(), "DebugGoChild");

			Assert.IsTrue(entry.IsInternal);
			Assert.AreEqual(typeof(Child), entry.Transition);
			Assert.IsNull(Find(context.Manager.FindState<Root>(), "Ping").Transition);
		}

		[Test]
		public async Task InternalEventStillDrivesTransition()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var root = context.Manager.FindState<Root>();
			EventCatalog.Invoke(root, Find(root, "DebugGoChild"), null);
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<Child>());
		}
	}
}
