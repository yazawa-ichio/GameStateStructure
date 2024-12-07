using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure.Tests
{
	partial class TransitionTest
	{
		[Push(typeof(TestProcess))]
		[Push(typeof(MainAA), "AA")]
		[Push(typeof(MainAB), "AB")]
		[GoTo(typeof(MainB))]
		partial class MainA : GameState
		{
			public void DoPushA() => PushAA<MainAA>();

			public void DoPushB() => PushAB<MainAB>();

			public Task DoPushProcess(Func<CancellationToken, Task> task) => Run<TestProcess>(task);

			public void DoGoToB() => GoTo<MainB>();
		}

		class TestProcess : GameState, IProcess
		{
			[Arg]
			Func<CancellationToken, Task> Task { get; set; }

			public async Task Run(CancellationToken token)
			{
				Debug.Log("Run:Start");
				await Task(token);
				Debug.Log("Run:End");
			}
		}

		[GoTo(typeof(MainAB))]
		partial class MainAA : GameState
		{
			public static int Count { get; private set; }

			protected override void OnEnter()
			{
				Count++;
			}

			public void DoPop() => Pop();
			public void DoGoToB() => GoTo<MainAB>();
		}

		[GoTo(typeof(MainAA))]
		partial class MainAB : GameState
		{
			public static int Count { get; private set; }

			protected override void OnEnter()
			{
				Count++;
			}

			public void DoPop() => Pop();
			public void DoGoTo() => GoTo<MainAA>();
		}

		[GoTo(typeof(MainA))]
		partial class MainB : GameState
		{
			public void DoGoToA() => GoTo<MainA>();

			internal void DoPop()
			{
				Pop();
			}
		}

		[Test]
		public async Task Test()
		{
			using var context = new TestContext();
			await context.Manager.Entry<MainA>();

			var mainA = context.Manager.FindState<MainA>();
			Assert.IsNotNull(mainA);
			mainA.DoGoToB();
			Assert.IsFalse(mainA.Active);

			var mainB = context.Manager.FindState<MainB>();
			Assert.IsNotNull(mainB);
			Assert.IsTrue(mainB.Active);
			Assert.ThrowsAsync<InvalidOperationException>(async () => { await mainA.DoPushProcess((token) => Task.CompletedTask); });

			mainB.DoGoToA();
			Assert.IsFalse(mainB.Active);
			mainA = context.Manager.FindState<MainA>();
			Assert.IsNotNull(mainA);
			{
				// 遷移時はロックされる

				var tcs = new TaskCompletionSource<bool>();
				var process = mainA.DoPushProcess((token) =>
				{
					token.Register(() => tcs.TrySetCanceled(token));
					return tcs.Task;
				});
				if (process.IsCompleted)
				{
					await process;
				}
				mainA.DoPushA();
				await Task.Yield();
				await Task.Yield();
				Assert.IsNotNull(context.Manager.FindState<TestProcess>());
				Assert.IsNull(context.Manager.FindState<MainAA>());
				tcs.SetResult(true);
				Assert.IsNull(context.Manager.FindState<TestProcess>());
				var aa = context.Manager.FindState<MainAA>();
				Assert.IsNotNull(aa);
				aa.DoGoToB();

				Assert.IsNotNull(context.Manager.FindState<MainAB>());
				Assert.IsNull(context.Manager.FindState<MainAA>());

				context.Manager.FindState<MainAB>().DoPop();
				Assert.IsNull(context.Manager.FindState<MainAB>());
			}
			{
				// Processは親の遷移ではキャンセルされる

				var tcs = new TaskCompletionSource<bool>();
				CancellationToken cancellationToken = default;
				var process = mainA.DoPushProcess((token) =>
				{
					cancellationToken = token;
					token.Register(() => tcs.TrySetCanceled(token));
					return tcs.Task;
				});
				if (process.IsCompleted)
				{
					await process;
				}

				var aaCount = MainAA.Count;
				var abCount = MainAB.Count;

				mainA.DoPushA();
				mainA.DoPushB();
				await Task.Yield();
				await Task.Yield();

				Assert.IsNotNull(context.Manager.FindState<TestProcess>());

				mainA.DoGoToB();
				await Task.Yield();

				Assert.IsTrue(cancellationToken.IsCancellationRequested);
				Assert.IsNull(context.Manager.FindState<TestProcess>());
				Assert.ThrowsAsync<TaskCanceledException>(async () => { await process; });
				Assert.AreEqual(aaCount, MainAA.Count);
				Assert.AreEqual(abCount, MainAB.Count);

			}
			{
				mainB = context.Manager.FindState<MainB>();
				Assert.IsNotNull(mainB);
				Assert.Throws<InvalidOperationException>(() => mainB.DoPop());
			}
		}

	}

}
