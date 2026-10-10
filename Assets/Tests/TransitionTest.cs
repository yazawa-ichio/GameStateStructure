using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure.Tests
{
	partial class TransitionTest
	{
		// 主軸の Push で畳まれずに走り続けることを見るため、車線に入れる
		[Push(typeof(TestProcess), "", Branch = typeof(TestBranch))]
		// 主軸に積まれる Process。兄弟を畳むことの検証に使う
		[Push(typeof(MainProcess), "MainProc")]
		[Push(typeof(MainAA), "AA")]
		[Push(typeof(MainAB), "AB")]
		[GoTo(typeof(MainB))]
		partial class MainA : GameState
		{
			public void DoPushA() => PushAA<MainAA>();

			public void DoPushB() => PushAB<MainAB>();

			public Task DoPushProcess(Func<CancellationToken, Task> task) => Run<TestProcess>(task);

			public Task DoPushMainProcess() => RunMainProc<MainProcess>();

			public void DoGoToB() => GoToMainB<MainB>();
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

		class MainProcess : GameState, IProcess
		{
			public Task Run(CancellationToken token) => System.Threading.Tasks.Task.CompletedTask;
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
			public void DoGoToB() => GoToMainAB<MainAB>();
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
			public void DoGoTo() => GoToMainAA<MainAA>();
		}

		[GoTo(typeof(MainA))]
		partial class MainB : GameState
		{
			public void DoGoToA() => GoToMainA<MainA>();

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
			// Assert.ThrowsAsync は IL2CPP + ストリッピングで NUnit 内部が壊れるため使わない
			var notCurrent = await TestAsync.Catch(() => mainA.DoPushProcess((token) => Task.CompletedTask));
			Assert.IsInstanceOf<InvalidOperationException>(notCurrent);

			mainB.DoGoToA();
			Assert.IsFalse(mainB.Active);
			mainA = context.Manager.FindState<MainA>();
			Assert.IsNotNull(mainA);
			{
				// Processの実行中の他の遷移時はロックされない

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
				Assert.IsNotNull(context.Manager.FindState<MainAA>());
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
				var cancelled = await TestAsync.Catch(() => process);
				Assert.IsInstanceOf<OperationCanceledException>(cancelled);
				Assert.AreEqual(aaCount + 1, MainAA.Count);
				Assert.AreEqual(abCount + 1, MainAB.Count);
			}
			{
				mainB = context.Manager.FindState<MainB>();
				Assert.IsNotNull(mainB);

				Exception exception = null;
				context.Manager.ErrorHandler = ErrorHandler.Create((Exception ex) =>
				{
					exception = ex;
				});
				mainB.DoPop();
				Assert.NotNull(exception);
				Assert.AreEqual(typeof(InvalidOperationException), exception?.GetType());
			}
		}

		[Test]
		public async Task MainAxisIsExclusiveTest()
		{
			using var context = new TestContext();
			await context.Manager.Entry<MainA>();

			var mainA = context.Manager.FindState<MainA>();
			Assert.IsNotNull(mainA);
			mainA.DoPushA();
			var aa = context.Manager.FindState<MainAA>();
			Assert.IsNotNull(aa);
			Assert.IsFalse(aa.Context.DisposeCancellationToken.IsCancellationRequested);
			mainA.DoPushB();
			Assert.IsTrue(aa.Context.DisposeCancellationToken.IsCancellationRequested);
			Assert.IsNull(context.Manager.FindState<MainAA>());
			var ab = context.Manager.FindState<MainAB>();
			Assert.IsNotNull(ab);
			Assert.IsFalse(ab.Context.DisposeCancellationToken.IsCancellationRequested);

			// 主軸に積まれる Process は、兄弟と同じ枠なので既存の子を畳む
			await mainA.DoPushMainProcess();

			Assert.IsTrue(ab.Context.DisposeCancellationToken.IsCancellationRequested);
		}
	}

}
