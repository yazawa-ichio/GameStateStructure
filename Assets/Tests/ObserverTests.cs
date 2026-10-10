using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// IStateObserver による遷移の観測の検証。
	/// </summary>
	public partial class ObserverTests
	{
		class Recorder : IStateObserver
		{
			public List<StateEvent> Events { get; } = new();

			public void OnStateEvent(in StateEvent e) => Events.Add(e);

			public string[] Format() => Events
				.Select(x => x.Type == StateEventType.Exit
					? $"{x.Type}:{Name(x)}:{x.Reason}"
					: $"{x.Type}:{Name(x)}")
				.ToArray();

			static string Name(StateEvent e) => e.State?.GetType().Name ?? "-";
		}

		class Thrower : IStateObserver
		{
			public void OnStateEvent(in StateEvent e) => throw new InvalidOperationException("observer failed");
		}

		[Push(typeof(Child), "Child")]
		partial class Root : GameState
		{
			public void DoPushChild() => PushChild<Child>();
		}

		partial class Child : GameState
		{
			public void DoPop() => Pop();
		}

		[Test]
		public async Task EnterAndExitAreObserved()
		{
			using var context = new TestContext();
			var recorder = new Recorder();
			context.Manager.AddObserver(recorder);

			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();
			context.Manager.FindState<Child>().DoPop();
			await context.WaitTransition();

			CollectionAssert.AreEqual(
				new[] { "Enter:Root", "Enter:Child", "Exit:Child:Pop" },
				recorder.Format());
		}

		[Test]
		public async Task ForceReleaseIsObservedOnReEntry()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();

			var recorder = new Recorder();
			context.Manager.AddObserver(recorder);

			await context.Entry<Root>();

			// 子から順に強制解体され、その後に新しいルートが入る
			CollectionAssert.AreEqual(
				new[] { "ForceRelease:Child", "ForceRelease:Root", "Enter:Root" },
				recorder.Format());
		}

		[Test]
		public async Task ObserverSurvivesEntry()
		{
			using var context = new TestContext();
			var recorder = new Recorder();
			context.Manager.AddObserver(recorder);

			await context.Entry<Root>();
			await context.Entry<Root>();

			// ErrorHandler と違い Entry でリセットされない
			Assert.AreEqual(3, recorder.Events.Count, string.Join(",", recorder.Format()));
		}

		[Test]
		public async Task ErrorIsObserved()
		{
			using var context = new TestContext();
			var recorder = new Recorder();
			context.Manager.AddObserver(recorder);
			await context.Entry<Root>();

			context.Manager.FindState<Root>().Context.Manage(new ThrowingDisposable());
			await context.Entry<Root>();

			Assert.IsTrue(recorder.Events.Any(x => x.Type == StateEventType.Error),
				string.Join(",", recorder.Format()));
			Assert.AreEqual("teardown failed",
				recorder.Events.First(x => x.Type == StateEventType.Error).Error.Message);
		}

		[Test]
		public async Task RemovedObserverStopsReceiving()
		{
			using var context = new TestContext();
			var recorder = new Recorder();
			context.Manager.AddObserver(recorder);

			await context.Entry<Root>();
			context.Manager.RemoveObserver(recorder);
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();

			CollectionAssert.AreEqual(new[] { "Enter:Root" }, recorder.Format());
		}

		[Test]
		public async Task ObserverExceptionDoesNotBreakTransition()
		{
			using var context = new TestContext();
			var recorder = new Recorder();
			context.Manager.AddObserver(new Thrower());
			context.Manager.AddObserver(recorder);

			LogAssert.ignoreFailingMessages = true;
			try
			{
				await context.Entry<Root>();
				context.Manager.FindState<Root>().DoPushChild();
				await context.WaitTransition();
			}
			finally
			{
				LogAssert.ignoreFailingMessages = false;
			}

			// 例外を投げる観測者がいても遷移は進み、他の観測者にも届く
			Assert.IsNotNull(context.Manager.FindState<Child>());
			CollectionAssert.AreEqual(new[] { "Enter:Root", "Enter:Child" }, recorder.Format());
		}

		class ThrowingDisposable : IDisposable
		{
			public void Dispose() => throw new InvalidOperationException("teardown failed");
		}
	}
}
