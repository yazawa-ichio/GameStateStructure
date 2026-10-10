using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// Entry（再起動）の堅牢性の検証。
	/// 解体で例外が出ても再起動は成功しなければならない。
	/// 失敗するとゲームが進行不能になるため。
	/// </summary>
	public partial class EntryRobustnessTests
	{
		class Probe : IDisposable
		{
			public int DisposeCount { get; private set; }

			public void Dispose() => DisposeCount++;
		}

		class ThrowingDisposable : IDisposable
		{
			readonly string m_Message;

			public ThrowingDisposable(string message) => m_Message = message;

			public void Dispose() => throw new InvalidOperationException(m_Message);
		}

		[Push(typeof(ChildA), "A")]
		[Push(typeof(ChildB), "B", Branch = typeof(TestBranch))]
		partial class Root : GameState
		{
			public void DoPushA() => PushA<ChildA>();

			public void DoPushB() => Handle(async () => await RunB<ChildB>());
		}

		class ChildA : GameState
		{
		}

		class ChildB : ResidentProcess
		{
		}

		[Test]
		public async Task EntryCompletesEvenIfTeardownThrows()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().Context.Manage(new ThrowingDisposable("teardown failed"));

			// 解体で例外が出ても Entry は完了しなければならない
			await context.Entry<Root>();

			Assert.IsNotNull(context.Manager.FindState<Root>());
		}

		[Test]
		public async Task TransitionsStillWorkAfterTeardownThrows()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			context.Manager.FindState<Root>().Context.Manage(new ThrowingDisposable("teardown failed"));

			await context.Entry<Root>();

			// AsyncLock が毒化されず、以後の遷移が通ること
			context.Manager.FindState<Root>().DoPushA();
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<ChildA>());
		}

		[Test]
		public async Task TeardownContinuesAfterOneStateFails()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushA();
			await context.WaitTransition();
			root.DoPushB();
			await context.WaitTransition();

			var probe = new Probe();
			context.Manager.FindState<ChildA>().Context.Manage(new ThrowingDisposable("child A failed"));
			context.Manager.FindState<ChildB>().Context.Manage(probe);

			await context.Entry<Root>();

			// 片方が失敗しても、もう片方の解放は行われる
			Assert.AreEqual(1, probe.DisposeCount);
		}

		[Test]
		public async Task TeardownErrorIsReportedToErrorHandler()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			context.Manager.FindState<Root>().Context.Manage(new ThrowingDisposable("teardown failed"));

			await context.Entry<Root>();

			Assert.IsNotNull(context.LastError, "握り潰さず ErrorHandler に報告する");
			StringAssert.Contains("teardown failed", context.LastError.Message);
		}
	}
}
