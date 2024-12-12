using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure.Tests
{
	public partial class GameStateStructureTests
	{

		[GoTo(typeof(Root))]
		[Push(typeof(MainA))]
		partial class Root : GameState
		{
			public void Reload()
			{
				GoToRoot<Root>();
			}

			public void PushA()
			{
				PushMainA<MainA>("A", 0, 0, this, null);
			}

		}

		[GoTo(typeof(MainB))]
		[Push(typeof(Process))]
		[Push(typeof(InitializeErrorTest), "Test")]
		partial class MainA : GameState
		{
			[Arg]
			public string A { get; set; }
			[Arg]
			public int aa { get; set; }
			[Arg]
			public long B { get; set; }
			[Arg]
			public object BB { get; set; }

			[Arg(Option = true)]
			public object Option { get; set; }

			public void ChangeB()
			{
				GoToMainB<MainB>();
			}

			public async Task ProcessTest(bool error)
			{
				await RunProcess<Process>(error);
			}

			public void InitializeError(bool error)
			{
				PushTest<InitializeErrorTest>(error);
			}

		}

		interface IEvent
		{
			[SubscribeEvent]
			void TestEvent(int a, float b, Vector3 c);
		}

		[Push(typeof(Child))]
		partial class MainB : GameState, IEvent
		{
			protected override void OnEnter()
			{
				PushChild<Child>();
			}

			[SubscribeEvent]
			public void PopChild()
			{
				Pop();
			}

			protected override void OnPopChild(GameState state)
			{
				Debug.Log("OnPopChild " + state);
			}

			[SubscribeEvent]
			public async ValueTask PopChildAsync()
			{
				Pop();
				await Task.Yield();
			}

			[SubscribeEvent]
			public async Task PopChildAsync2()
			{
				Pop();
				await Task.Yield();
			}

			[SubscribeEvent(Broadcast = true, ChildOnly = true)]
			public async UniTask PopChildAsync3()
			{
				Pop();
				await UniTask.Yield();
			}

			[SubscribeEvent]
			public async void PopChildAsync4()
			{
				Pop();
				await Task.Yield();
			}

			void IEvent.TestEvent(int a, float b, Vector3 c)
			{
				throw new NotImplementedException();
			}
		}

		partial class InitializeErrorTest : GameState
		{
			[Arg]
			bool Error { get; set; } = true;

			protected override async Task OnInitialize()
			{
				await Task.Delay(500, Context.DisposeCancellationToken);
				if (Error)
				{
					throw new Exception("Error");
				}
				Pop();
			}

		}

		[PublishEvent(typeof(MainB), Prefix = "Dispatch")]
		[PublishEvent(typeof(IEvent))]
		partial class Child : GameState
		{
			public void Dispatch()
			{
				DispatchPopChild();
			}

			void Test()
			{
				_ = DispatchPopChildAsync();
				_ = DispatchPopChildAsync2();
				_ = DispatchPopChildAsync3();
				DispatchPopChildAsync4();
				TestEvent(1, 2, Vector3.zero);
			}
		}

		partial class Process : GameState, IProcess
		{
			[Arg]
			bool Error { get; set; }

			public async Task Run(CancellationToken token)
			{
				await Task.Delay(100, token);
				if (Error)
				{
					throw new Exception("Error");
				}
			}
		}



		[Test]
		public async Task Tests()
		{
			using (var context = new TestContext())
			{
				await context.Manager.Entry<Root>();
				var root = context.Manager.FindAllStates<Root>().FirstOrDefault();
				Assert.NotNull(root);
				root.PushA();
				var mainA = context.Manager.FindAllStates<MainA>().FirstOrDefault();
				Assert.NotNull(mainA);
				mainA.ChangeB();
				var child = context.Manager.FindAllStates<Child>().FirstOrDefault();
				Assert.NotNull(child);
				child.Dispatch();
				Assert.IsNull(context.Manager.FindAllStates<MainB>().FirstOrDefault());
			}
		}

		[Test]
		public async Task ErrorTests()
		{
			using (var context = new TestContext())
			{
				await context.Manager.Entry<Root>();
				context.Manager.ErrorHandler = new TestErrorHandler();
				var root = context.Manager.FindAllStates<Root>().FirstOrDefault();
				root.PushA();
				var mainA = context.Manager.FindAllStates<MainA>().FirstOrDefault();
				Assert.NotNull(mainA);
				await mainA.ProcessTest(error: false);
				Assert.Null(context.Manager.FindAllStates<Process>().FirstOrDefault());
				try
				{
					await mainA.ProcessTest(error: true);
					Assert.Fail();
				}
				catch (Exception err)
				{
					Assert.AreEqual("Error", err.Message);
				}
				Assert.Null(context.Manager.FindAllStates<Process>().FirstOrDefault());
				{
					TestErrorHandler.Error = null;
					mainA.InitializeError(false);
					await Task.Delay(1000);
					Assert.IsNull(TestErrorHandler.Error);
					Assert.Null(context.Manager.FindAllStates<InitializeErrorTest>().FirstOrDefault());
				}
				{
					TestErrorHandler.Error = null;
					mainA.InitializeError(true);
					await Task.Delay(1000);
					Assert.IsNotNull(TestErrorHandler.Error);
					Assert.Null(context.Manager.FindAllStates<InitializeErrorTest>().FirstOrDefault());
				}

				try
				{
					await mainA.ProcessTest(error: false);
					Assert.Fail();
				}
				catch (TransitionHangException err)
				{
					// ハングしたエラーが発生する
					Assert.AreEqual("transition hang : Error", err.Message);
					Assert.AreEqual(TestErrorHandler.Error, err.InnerException);
				}

				{
					// 再エントリーすればエラーが解消する
					await context.Manager.Entry<Root>();
					context.Manager.ErrorHandler = new TestErrorHandler();
					root = context.Manager.FindAllStates<Root>().FirstOrDefault();
					root.PushA();
					mainA = context.Manager.FindAllStates<MainA>().FirstOrDefault();
					Assert.NotNull(mainA);
					await mainA.ProcessTest(error: false);
				}
			}
		}


	}

}
