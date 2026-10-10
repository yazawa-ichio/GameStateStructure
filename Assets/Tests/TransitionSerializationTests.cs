using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// 遷移が直列化されることの検証。
	/// <para>
	/// 遷移はすべて <c>AsyncLock</c> を取ってから進むため、
	/// 前の遷移が完了するまで次の遷移は開始しない。
	/// このライブラリの中核の保証だが、実際に競合させた検証が無かった。
	/// </para>
	/// </summary>
	public partial class TransitionSerializationTests
	{
		static List<string> s_Order;

		[SetUp]
		public void SetUp() => s_Order = new List<string>();

		[Push(typeof(Slow), "Slow")]
		[Push(typeof(Fast), "Fast")]
		partial class Root : GameState
		{
			public void DoPushSlow() => PushSlow<Slow>();

			public void DoPushFast() => PushFast<Fast>();
		}

		/// <summary>初期化で止まり、外から解放されるまで遷移を占有する。</summary>
		partial class Slow : GameState
		{
			static TaskCompletionSource<bool> s_Gate;

			public static void Reset() => s_Gate = new TaskCompletionSource<bool>(
				TaskCreationOptions.RunContinuationsAsynchronously);

			public static void Release() => s_Gate.TrySetResult(true);

			protected override Task OnInitialize()
			{
				s_Order.Add("Slow.Init");
				return s_Gate.Task;
			}

			protected override void OnEnter() => s_Order.Add("Slow.Enter");

			protected override Task OnExit()
			{
				s_Order.Add("Slow.Exit");
				return Task.CompletedTask;
			}
		}

		partial class Fast : GameState
		{
			protected override Task OnInitialize()
			{
				s_Order.Add("Fast.Init");
				return Task.CompletedTask;
			}

			protected override void OnEnter() => s_Order.Add("Fast.Enter");
		}

		[Test]
		public async Task SecondTransitionWaitsForTheFirstToComplete()
		{
			Slow.Reset();
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushSlow();
			await context.Tick(2);

			CollectionAssert.AreEqual(new[] { "Slow.Init" }, s_Order, "最初の遷移が初期化で止まっている");
			Assert.IsTrue(context.Manager.IsBusy, "ロックが保持されている");

			// 止まっている間に次の遷移を要求する
			root.DoPushFast();
			await context.Tick(3);

			CollectionAssert.AreEqual(new[] { "Slow.Init" }, s_Order,
				"前の遷移が終わるまで次の初期化は始まらない");
			Assert.IsNull(context.Manager.FindState<Fast>());

			Slow.Release();
			await context.WaitTransition();

			// 主軸は排他なので、Fast の Push で Slow は畳まれる
			CollectionAssert.AreEqual(
				new[] { "Slow.Init", "Slow.Enter", "Fast.Init", "Slow.Exit", "Fast.Enter" },
				s_Order,
				"順に処理され、途中の状態が混ざらない");
			Assert.IsNotNull(context.Manager.FindState<Fast>());
			Assert.IsNull(context.Manager.FindState<Slow>());
		}

		[Test]
		public async Task ManagerIsBusyWhileATransitionIsBlocked()
		{
			Slow.Reset();
			using var context = new TestContext();
			await context.Entry<Root>();

			Assert.IsFalse(context.Manager.IsBusy);

			context.Manager.FindState<Root>().DoPushSlow();
			await context.Tick(2);
			Assert.IsTrue(context.Manager.IsBusy);

			Slow.Release();
			await context.WaitTransition();
			Assert.IsFalse(context.Manager.IsBusy);
		}
	}
}
