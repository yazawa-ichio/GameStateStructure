using NUnit.Framework;
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameStateStructure.Tests
{
	public partial class GameStateStructureTests
	{
		class Context : IDisposable
		{
			GameObject Owner { get; }

			public GameStateManager Manager { get; }

			public Context()
			{
				Owner = new GameObject("Context");
				Manager = Owner.AddComponent<GameStateManager>();
				Manager.ErrorHandler = new ErrorHandler();
			}

			public void Dispose()
			{
				GameObject.Destroy(Owner);
			}

		}

		IEnumerator Async(Func<Task> task)
		{
			var t = task();
			while (!t.IsCompleted)
			{
				yield return null;
			}
			if (t.IsFaulted)
			{
				Assert.Fail(t.Exception.ToString());
			}
		}

		[GoTo(typeof(Root))]
		[Push(typeof(MainA))]
		partial class Root : GameState
		{
			public void Reload()
			{
				GoToRoot();
			}

			public void PushA()
			{
				PushMainA("A", 0, 0, this, null);
			}

		}

		[GoTo(typeof(MainB))]
		[Push(typeof(Modal))]
		[Push(typeof(InitializeErrorTest))]
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
				GoToMainB();
			}

			public async Task ModalTest(bool error)
			{
				await RunModal(error);
			}

			public void InitializeError(bool error, CancellationToken token)
			{
				PushInitializeErrorTest(error, token);
			}

		}

		[Push(typeof(Child))]
		partial class MainB : GameState
		{
			protected override void OnEnter()
			{
				PushChild();
			}

			[SubscribeEvent]
			public void PopChild()
			{
				Pop();
			}
		}

		partial class InitializeErrorTest : GameState
		{
			[Arg]
			bool Error { get; set; } = true;

			protected override async Task OnInitialize()
			{
				await Task.Delay(500, Context.CancellationToken);
				if (Error)
				{
					throw new Exception("Error");
				}
				Pop();
			}

		}

		[PublishEvent(typeof(MainB), Prefix = "Dispatch")]
		partial class Child : GameState
		{
			public void Dispatch()
			{
				DispatchPopChild();
			}
		}

		partial class Modal : GameState, IModule
		{
			[Arg]
			bool Error { get; set; }

			public async Task Run()
			{
				await Task.Delay(100);
				if (Error)
				{
					throw new Exception("Error");
				}
			}
		}

		class ErrorHandler : IErrorHandler
		{
			public static Exception Error;

			public void Handle(Exception error)
			{
				Error = error;
			}
		}


		[UnityTest]
		public IEnumerator Tests() => Async(async () =>
		{
			using (var context = new Context())
			{
				await context.Manager.Entry<Root>();
				var root = context.Manager.GetAllStates<Root>().FirstOrDefault();
				Assert.NotNull(root);
				root.PushA();
				var mainA = context.Manager.GetAllStates<MainA>().FirstOrDefault();
				Assert.NotNull(mainA);
				mainA.ChangeB();
				var child = context.Manager.GetAllStates<Child>().FirstOrDefault();
				Assert.NotNull(child);
				child.Dispatch();
				Assert.IsNull(context.Manager.GetAllStates<MainB>().FirstOrDefault());
			}
		});

		[UnityTest]
		public IEnumerator ErrorTests() => Async(async () =>
		{
			using (var context = new Context())
			{
				await context.Manager.Entry<Root>();
				context.Manager.ErrorHandler = new ErrorHandler();
				var root = context.Manager.GetAllStates<Root>().FirstOrDefault();
				root.PushA();
				var mainA = context.Manager.GetAllStates<MainA>().FirstOrDefault();
				Assert.NotNull(mainA);
				await mainA.ModalTest(error: false);
				Assert.Null(context.Manager.GetAllStates<Modal>().FirstOrDefault());
				try
				{
					await mainA.ModalTest(error: true);
					Assert.Fail();
				}
				catch (Exception err)
				{
					Assert.AreEqual("Error", err.Message);
				}
				Assert.Null(context.Manager.GetAllStates<Modal>().FirstOrDefault());
				{
					ErrorHandler.Error = null;
					mainA.InitializeError(false, CancellationToken.None);
					await Task.Delay(1000);
					Assert.IsNull(ErrorHandler.Error);
					Assert.Null(context.Manager.GetAllStates<InitializeErrorTest>().FirstOrDefault());
				}
				{
					ErrorHandler.Error = null;
					mainA.InitializeError(true, CancellationToken.None);
					await Task.Delay(1000);
					Assert.IsNotNull(ErrorHandler.Error);
					Assert.Null(context.Manager.GetAllStates<InitializeErrorTest>().FirstOrDefault());
				}
				{
					CancellationTokenSource cancellationTokenSource = new();
					mainA.InitializeError(true, cancellationTokenSource.Token);
					await Task.Delay(100);
					cancellationTokenSource.Cancel();
					await Task.Delay(100);
					Assert.Null(context.Manager.GetAllStates<InitializeErrorTest>().FirstOrDefault());
				}
			}
		});


	}
}
