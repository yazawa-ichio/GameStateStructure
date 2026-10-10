using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// 完了を待たない非同期処理を投げる <c>GameState.Handle</c> の検証。
	/// <para>
	/// UniTask の有無で引数の型が変わるが、挙動は同じでなければならない。
	/// ラムダを渡す形にして、どちらの構成でも同じテストが通るようにしている。
	/// </para>
	/// </summary>
	public partial class HandleTests
	{
		partial class Root : GameState
		{
#if GSS_UNITASK && !GSS_DISABLE_UNITASK
			public void Run(Func<Cysharp.Threading.Tasks.UniTask> task) => Handle(task);

			public void Run(Func<Cysharp.Threading.Tasks.UniTask> task, bool ignoreCancel)
				=> Handle(task, ignoreCancel);
#else
			public void Run(Func<Task> task) => Handle(task);

			public void Run(Func<Task> task, bool ignoreCancel) => Handle(task, ignoreCancel);
#endif
		}

		[Test]
		public async Task ErrorIsReportedToErrorHandler()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().Run(() => throw new InvalidOperationException("boom"));
			await context.Tick(2);

			Assert.IsNotNull(context.LastError);
			StringAssert.Contains("boom", context.LastError.Message);
		}

		[Test]
		public async Task CancellationIsIgnoredByDefault()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().Run(() => throw new OperationCanceledException());
			await context.Tick(2);

			Assert.IsNull(context.LastError, "既定の ignoreCancel = true でキャンセルは握られる");
		}

		[Test]
		public async Task CancellationIsReportedWhenNotIgnored()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().Run(() => throw new OperationCanceledException(), false);
			await context.Tick(2);

			Assert.IsInstanceOf<OperationCanceledException>(context.LastError);
		}

		[Test]
		public async Task AsyncBodyRunsToCompletion()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var ran = false;

			context.Manager.FindState<Root>().Run(async () =>
			{
				await Task.Yield();
				ran = true;
			});
			await context.Tick(4);

			Assert.IsTrue(ran);
			Assert.IsNull(context.LastError);
		}

		[Test]
		public async Task ErrorAfterAwaitIsReported()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().Run(async () =>
			{
				await Task.Yield();
				throw new InvalidOperationException("late boom");
			});
			await context.Tick(4);

			Assert.IsNotNull(context.LastError);
			StringAssert.Contains("late boom", context.LastError.Message);
		}
	}
}
