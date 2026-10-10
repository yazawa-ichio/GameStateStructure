using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// Context によるリソース解放の検証。
	/// </summary>
	public partial class ContextTests
	{
		static List<string> s_Order;

		[SetUp]
		public void SetUp() => s_Order = new List<string>();

		class Probe : IDisposable
		{
			public int DisposeCount { get; private set; }

			public void Dispose() => DisposeCount++;
		}

		class ThrowingDisposable : IDisposable
		{
			readonly string m_Message;

			public bool Called { get; private set; }

			public ThrowingDisposable(string message = "dispose failed") => m_Message = message;

			public void Dispose()
			{
				Called = true;
				throw new InvalidOperationException(m_Message);
			}
		}

		class ExitTaskProbe : IExitTask
		{
			public bool Completed { get; private set; }

			public bool Forced { get; private set; }

			public async Task Run(CancellationToken ct)
			{
				await Task.Delay(30);
				Completed = true;
				s_Order.Add("ExitTask");
			}

			public void ForceRelease()
			{
				Forced = true;
				s_Order.Add("ForceRelease");
			}
		}

		[Push(typeof(Child), "Child")]
		partial class Root : GameState
		{
			public void DoPushChild() => PushChild<Child>();
		}

		partial class Child : GameState
		{
			public bool ExitCalled { get; private set; }

			protected override Task OnExit()
			{
				ExitCalled = true;
				s_Order.Add("OnExit");
				return Task.CompletedTask;
			}

			public void DoPop() => Pop();
		}

		async Task<Child> PushChild(TestContext context)
		{
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();
			return context.Manager.FindState<Child>();
		}

		[Test]
		public async Task ManagedDisposableIsDisposedOnceOnExit()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var probe = new Probe();
			child.Context.Manage(probe);
			Assert.AreEqual(0, probe.DisposeCount);

			child.DoPop();
			await context.WaitTransition();

			Assert.AreEqual(1, probe.DisposeCount);
		}

		[Test]
		public async Task RegisteredExitTaskIsAwaitedBeforeOnExit()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var probe = new ExitTaskProbe();
			child.Context.RegisterExitTask(probe);

			child.DoPop();
			await context.WaitTransition();

			Assert.IsTrue(probe.Completed, "終了時に完了まで await される");
			CollectionAssert.AreEqual(new[] { "ExitTask", "OnExit" }, s_Order,
				"OnPreExit の後、OnExit の前に実行される");
		}

		[Test]
		public async Task UnregisteredExitTaskIsNotRun()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var probe = new ExitTaskProbe();
			child.Context.RegisterExitTask(probe);
			child.Context.UnregisterExitTask(probe);

			child.DoPop();
			await context.WaitTransition();

			Assert.IsFalse(probe.Completed);
			CollectionAssert.AreEqual(new[] { "OnExit" }, s_Order);
		}

		[Test]
		public async Task ExitTaskFallsBackToForceReleaseOnReEntry()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var probe = new ExitTaskProbe();
			child.Context.RegisterExitTask(probe);

			await context.Entry<Root>();

			Assert.IsFalse(probe.Completed, "強制解放では Run を待たない");
			Assert.IsTrue(probe.Forced, "代わりに ForceRelease が呼ばれる");
			// Entry 再入では OnExit も呼ばれない
			CollectionAssert.AreEqual(new[] { "ForceRelease" }, s_Order);
		}

		[Test]
		public async Task ManagedGameObjectAndComponentAreDestroyed()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var managedObject = new GameObject("managed-object");
			child.Context.Manage(managedObject);

			var holder = new GameObject("component-holder");
			var component = holder.AddComponent<BoxCollider>();
			child.Context.ManageComponent(component);

			child.DoPop();
			await context.WaitTransition();
			// Destroy はフレーム末まで遅延する
			await context.Tick(2);

			Assert.IsTrue(managedObject == null, "Manage(GameObject) は GameObject を破棄する");
			Assert.IsTrue(component == null, "ManageComponent は Component のみ破棄する");
			Assert.IsFalse(holder == null, "Component の所有者は破棄されない");

			GameObject.DestroyImmediate(holder);
		}

		[Test]
		public async Task DisposeCancellationTokenFiresOnExit()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var token = child.Context.DisposeCancellationToken;
			Assert.IsFalse(token.IsCancellationRequested);

			child.DoPop();
			await context.WaitTransition();

			Assert.IsTrue(token.IsCancellationRequested);
		}

		[Test]
		public async Task ManageAfterDisposeReleasesImmediately()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			child.DoPop();
			await context.WaitTransition();

			// 所有者が居ないため、登録ではなく即座の解放になる
			var probe = new Probe();
			Assert.AreSame(probe, child.Context.Manage(probe));
			Assert.AreEqual(1, probe.DisposeCount);

			var ran = false;
			var returned = child.Context.Manage(() => ran = true);
			Assert.IsTrue(ran);
			Assert.IsNotNull(returned, "登録は無いが戻り値は null にしない");
			returned.Dispose();
			Assert.IsTrue(ran, "返された IDisposable は何もしない");

			// ManageComponent も Manage(GameObject) と同じく即座に破棄する
			var holder = new GameObject("late-holder");
			var component = holder.AddComponent<BoxCollider>();
			Assert.AreSame(component, child.Context.ManageComponent(component));
			// Destroy はフレーム末まで遅延する
			await context.Tick(2);
			Assert.IsTrue(component == null, "即座に Destroy される");

			GameObject.DestroyImmediate(holder);
		}

		[Test]
		public async Task RegisterExitTaskAfterDisposeThrows()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			child.DoPop();
			await context.WaitTransition();

			// 「終了時に走らせる」という意図は、終了済みでは達成できない。
			// Manage と違い、即座に処理しても意図を満たさないため例外にする。
			Assert.Throws<ObjectDisposedException>(
				() => child.Context.RegisterExitTask(new NoopExitTask()));
		}

		class NoopExitTask : IExitTask
		{
			public Task Run(CancellationToken ct) => Task.CompletedTask;

			public void ForceRelease() { }
		}

		[Test]
		public async Task MultipleDisposeFailuresAreAggregatedAndAllAreAttempted()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			var first = new ThrowingDisposable("first failed");
			var second = new ThrowingDisposable("second failed");
			var probe = new Probe();
			child.Context.Manage(first);
			child.Context.Manage(second);
			child.Context.Manage(probe);

			child.DoPop();
			await context.WaitTransition();

			// 1 つ目が投げても後続の解放は続行される
			Assert.IsTrue(first.Called);
			Assert.IsTrue(second.Called, "前の Dispose が投げても次は実行される");
			Assert.AreEqual(1, probe.DisposeCount, "例外を投げないものも解放される");

			var aggregate = context.LastError as AggregateException;
			Assert.IsNotNull(aggregate, "複数の例外は AggregateException にまとめられる: " + context.LastError);
			Assert.AreEqual(2, aggregate.InnerExceptions.Count);
			CollectionAssert.AreEquivalent(
				new[] { "first failed", "second failed" },
				aggregate.InnerExceptions.Select(x => x.Message).ToArray());
		}

		[Test]
		public async Task ExitTasksStillRunWhenDisposeThrows()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			child.Context.Manage(new ThrowingDisposable());
			var task = new ExitTaskProbe();
			child.Context.RegisterExitTask(task);

			child.DoPop();
			// 解体はロックを保持したまま進むので、解放されるまで待てば完了している
			await context.WaitTransition();

			// DisposeManaged は例外を集めるだけで投げないため、
			// IExitTask.Run はスキップされない
			Assert.IsTrue(task.Completed, "Dispose が投げても IExitTask は実行される");
			Assert.IsFalse(child.ExitCalled, "スキップされるのは OnExit だけ");
		}

		[Test]
		public async Task ThrowingDisposableSkipsOnExitAndPoisonsTheManager()
		{
			using var context = new TestContext();
			var child = await PushChild(context);

			child.Context.Manage(new ThrowingDisposable());

			child.DoPop();
			await context.Tick(3);

			// DoExit は OnPreExit → Context.DisposeAsync → OnExit の順。
			// DisposeAsync が投げると OnExit に到達しない。
			Assert.IsFalse(child.ExitCalled, "Dispose が例外を投げると OnExit が呼ばれない");
			Assert.IsNotNull(context.LastError);

			// さらに例外が Handle まで伝播するため AsyncLock が毒化される
			context.Manager.FindState<Root>().DoPushChild();
			await context.Tick(3);
			Assert.IsNull(context.Manager.FindState<Child>(), "以後の遷移が通らなくなる");
		}
	}
}
