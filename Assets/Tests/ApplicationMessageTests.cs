using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// アプリの中断・復帰が全ステートに配送されることの検証。
	/// <para>
	/// Unity のメッセージ（<c>OnApplicationPause</c> / <c>OnApplicationFocus</c>）は
	/// バッチ実行では発生しないため、<c>SendMessage</c> で手動発火する。
	/// </para>
	/// </summary>
	public partial class ApplicationMessageTests
	{
		static List<string> s_Log;

		[SetUp]
		public void SetUp() => s_Log = new List<string>();

		abstract class Recorder : GameState
		{
			protected override void OnApplicationPause(bool pause)
				=> s_Log.Add($"{GetType().Name}.Pause({pause})");

			protected override void OnApplicationFocus(bool focus)
				=> s_Log.Add($"{GetType().Name}.Focus({focus})");
		}

		[Push(typeof(Mid), "Mid")]
		partial class Root : Recorder
		{
			public void DoPushMid() => PushMid<Mid>();
		}

		[Push(typeof(Leaf), "Leaf")]
		partial class Mid : Recorder
		{
			public void DoPushLeaf() => PushLeaf<Leaf>();
		}

		class Leaf : Recorder
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
		public async Task PauseReachesEveryState()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			context.Manager.SendMessage("OnApplicationPause", true);

			CollectionAssert.AreEquivalent(
				new[] { "Root.Pause(True)", "Mid.Pause(True)", "Leaf.Pause(True)" },
				s_Log,
				"深さに関わらず全てのステートに届く");
		}

		[Test]
		public async Task FocusReachesEveryState()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			context.Manager.SendMessage("OnApplicationFocus", false);

			CollectionAssert.AreEquivalent(
				new[] { "Root.Focus(False)", "Mid.Focus(False)", "Leaf.Focus(False)" },
				s_Log);
		}

		[Test]
		public async Task ArgumentIsPassedThrough()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.SendMessage("OnApplicationPause", true);
			context.Manager.SendMessage("OnApplicationPause", false);

			CollectionAssert.AreEqual(
				new[] { "Root.Pause(True)", "Root.Pause(False)" },
				s_Log);
		}

		[Test]
		public async Task PoppedStateNoLongerReceivesMessages()
		{
			using var context = new TestContext();
			await BuildThreeLevels(context);

			// Mid を畳むと Leaf も一緒に消える
			context.Manager.FindState<Root>().DoPushMid();
			await context.WaitTransition();
			s_Log.Clear();

			context.Manager.SendMessage("OnApplicationPause", true);

			CollectionAssert.AreEquivalent(
				new[] { "Root.Pause(True)", "Mid.Pause(True)" },
				s_Log,
				"破棄されたステートには届かない");
		}
	}
}
