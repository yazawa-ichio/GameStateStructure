using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// IProcess の Push が排他処理を通るかの確認。
	/// モーダルとして重ねたいのか、既存の子を畳むのかは設計上の分かれ目になる。
	/// </summary>
	public partial class ProcessExclusiveTests
	{
		[Push(typeof(Sub), "Sub")]
		[Push(typeof(Modal), "Modal")]
		partial class Host : GameState
		{
			public void DoPushSub() => PushSub<Sub>();

			public Task DoRunModal(TaskCompletionSource<bool> gate) => RunModal<Modal>(gate);
		}

		class Sub : GameState
		{
		}

		partial class Modal : GameState, IProcess
		{
			[Arg]
			public TaskCompletionSource<bool> Gate { get; set; }

			public Task Run(CancellationToken ct) => Gate.Task;
		}

		[Test]
		public async Task ProcessPushExitsExistingChild()
		{
			using var context = new TestContext();
			await context.Entry<Host>();

			var host = context.Manager.FindState<Host>();
			host.DoPushSub();
			await context.WaitTransition();
			Assert.IsNotNull(context.Manager.FindState<Sub>());

			var gate = new TaskCompletionSource<bool>();
			var running = host.DoRunModal(gate);
			await context.Tick(2);

			// 既定は排他なので、モーダルを出すと既存の子が畳まれる
			Assert.IsNotNull(context.Manager.FindState<Modal>(), "Modal は積まれる");
			Assert.IsNull(context.Manager.FindState<Sub>(), "既存の子が消える");

			gate.SetResult(true);
			await running;
		}
	}
}
