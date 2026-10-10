using NUnit.Framework;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// Entry の再入、ブランチによる並存、生成コードの型引数など、
	/// 遷移まわりの制御の検証。
	/// </summary>
	public partial class TransitionControlTests
	{
		[Push(typeof(Persistent), "Persistent", Branch = typeof(TestBranch))]
		[Push(typeof(Transient), "Transient")]
		[Push(typeof(BaseChild), "Base")]
		partial class Root : GameState
		{
			public bool ExitCalled { get; private set; }

			public bool PreExitCalled { get; private set; }

			protected override Task OnPreExit()
			{
				PreExitCalled = true;
				return Task.CompletedTask;
			}

			protected override Task OnExit()
			{
				ExitCalled = true;
				return Task.CompletedTask;
			}

			public void DoPushPersistent() => Handle(async () => await RunPersistent<Persistent>());

			public void DoPushTransient() => PushTransient<Transient>();

			// 型引数に派生型を渡しても、生成されるのは宣言された型のまま
			public void DoPushDerived() => PushBase<DerivedChild>();
		}

		class Persistent : ResidentProcess
		{
		}

		class Transient : GameState
		{
		}

		class BaseChild : GameState
		{
		}

		class DerivedChild : BaseChild
		{
		}

		[Test]
		public async Task BranchChildSurvivesMainAxisPush()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			var root = context.Manager.FindState<Root>();

			root.DoPushPersistent();
			await context.WaitTransition();
			root.DoPushTransient();
			await context.WaitTransition();

			// 主軸は排他だが、別の車線に入った子は畳まれない
			Assert.IsNotNull(context.Manager.FindState<Persistent>());
			Assert.IsNotNull(context.Manager.FindState<Transient>());

			var firstTransient = context.Manager.FindState<Transient>();
			root.DoPushTransient();
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<Persistent>(), "車線が違えば繰り返しても畳まれない");
			Assert.AreNotSame(firstTransient, context.Manager.FindState<Transient>(), "主軸の方は差し替わる");
		}

		[Test]
		public async Task ReEntrySkipsExitCallbacksButDisposesContext()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			var first = context.Manager.FindState<Root>();
			var token = first.Context.DisposeCancellationToken;

			await context.Entry<Root>();

			// Entry は ForceRelease を使い、DoExit を経由しない。
			// そのため OnPreExit / OnExit は呼ばれない。
			Assert.IsFalse(first.PreExitCalled, "Entry 再入では OnPreExit が呼ばれない");
			Assert.IsFalse(first.ExitCalled, "Entry 再入では OnExit が呼ばれない");

			// 一方 Context は解放される
			Assert.IsTrue(token.IsCancellationRequested);

			Assert.AreNotSame(first, context.Manager.FindState<Root>());
		}

		[Test]
		public async Task TypeArgumentDoesNotChangeCreatedType()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().DoPushDerived();
			await context.WaitTransition();

			// PushBase<DerivedChild>() と書けてしまうが、生成されるのは BaseChild。
			// 生成コードが型引数を使わず宣言型を直接指定しているため。
			var created = context.Manager.FindState<BaseChild>();
			Assert.IsNotNull(created);
			Assert.AreEqual(typeof(BaseChild), created.GetType());
			Assert.IsNull(context.Manager.FindState<DerivedChild>());
		}

		[Test]
		public async Task ManagerReportsBusyWhileTransitioning()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			Assert.IsFalse(context.Manager.IsBusy, "遷移が無い間は false");

			context.Manager.FindState<Root>().DoPushPersistent();
			await context.WaitTransition();

			Assert.IsFalse(context.Manager.IsBusy);
			Assert.IsNotNull(context.Manager.FindState<Persistent>());
		}

		[Test]
		public async Task TransitionsAfterDestroyAreSwallowed()
		{
			var owner = new GameObject("destroy-target");
			var manager = owner.AddComponent<GameStateManager>();
			var handler = new TestErrorHandler();

			await manager.Entry<Root>(new ParameterHolder(), new Config { ErrorHandler = handler });
			var root = manager.FindState<Root>();

			// OnDestroy を同期で走らせる
			GameObject.DestroyImmediate(owner);

			root.DoPushPersistent();
			await Task.Yield();

			// 破棄後の遷移失敗は ErrorHandler ではなく警告ログに落とされる
			Assert.IsNull(handler.Error, "破棄後のエラーは ErrorHandler に流さない");
		}
	}
}
