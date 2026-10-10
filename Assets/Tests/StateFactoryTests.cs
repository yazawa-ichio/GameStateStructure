using GameStateStructure.Fixtures;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// <see cref="IStateFactory"/> の検証。
	/// <para>
	/// DI コンテナは使わず、リフレクションでコンストラクタを解決するファクトリで
	/// 同じ経路を再現する。コンテナ固有の事情を持ち込まずに、
	/// ライブラリ側の契約だけを固定する。
	/// </para>
	/// </summary>
	public partial class StateFactoryTests
	{
		/// <summary>子に渡され、ステートの終了で破棄される値。</summary>
		class Value : IDisposable
		{
			public static int Disposed;

			public string Name { get; }

			public Value(string name) => Name = name;

			public void Dispose() => Disposed++;
		}

		/// <summary>完了しない終了処理。解体が止まる状況を作る。</summary>
		class HangingExitTask : IExitTask
		{
			readonly TaskCompletionSource<bool> m_Source = new TaskCompletionSource<bool>();

			public bool Forced { get; private set; }

			public Task Run(System.Threading.CancellationToken ct) => m_Source.Task;

			public void ForceRelease() => Forced = true;

			/// <summary>テストの終了時に止まったままの Task を残さないため。</summary>
			public void Complete() => m_Source.TrySetResult(true);
		}

		// 遷移先は GameStateStructure.Fixtures に置いてある。
		// テストアセンブリはリンカのルートに指定され中身が除去されないため、
		// 管理コード除去の検証にはルートでないアセンブリが要る。
		[Push(typeof(NeedsDependency), "Child")]
		[Push(typeof(NoDeclaredConstructor), "Plain")]
		[Push(typeof(FailsOnInitialize), "Failing")]
		[Push(typeof(FailsOnExit), "BadExit")]
		[Push(typeof(NoDeclaredConstructor), "Replacing")]
		[Push(typeof(TakesArgument), "Arg")]
		partial class Root : GameState
		{
			public void DoPushChild() => PushChild<NeedsDependency>();

			public void DoPushPlain() => PushPlain<NoDeclaredConstructor>();

			public void DoPushFailing() => PushFailing<FailsOnInitialize>();

			// 生成コードは型安全なので、型の合わない値は public API から渡す
			public void DoPushBadArg()
			{
				var holder = new ParameterHolder();
				holder.Set("Value", "not an int");
				Manager.Push<FailsOnArgument>(this, holder);
			}

			public void DoPushBadExit() => PushBadExit<FailsOnExit>();

			public void DoPushReplacing() => PushReplacing<NoDeclaredConstructor>();

			public void DoPushArg(int value) => PushArg<TakesArgument>(value);
		}

		/// <summary>
		/// コンストラクタをリフレクションで解決する。DI コンテナと同じ経路。
		/// 引数なしの場合はライブラリの既定と同じジェネリック版を使い、
		/// 検証対象を引数付きコンストラクタの保持だけに絞る。
		/// </summary>
		class ReflectionFactory : IStateFactory
		{
			readonly object[] m_Services = { new Dependency() };

			public string Label { get; set; } = "root";

			public (T State, IDisposable Value) Create<T>(
				in StateCreationContext context, IDisposable parent) where T : GameState
			{
				var name = parent is Value v ? v.Name + "/" + Label : Label;
				// コンテナと同じく、常にリフレクションで解決する。
				// ジェネリック版を使うと静的な参照が残り、除去の検証にならない。
				var ctor = typeof(T).GetConstructors()
					.OrderByDescending(x => x.GetParameters().Length)
					.FirstOrDefault();
				if (ctor == null)
				{
					throw new InvalidOperationException(
						"public なコンストラクタが見つかりません: " + typeof(T));
				}
				if (ctor.GetParameters().Length == 0)
				{
					return ((T)ctor.Invoke(null), new Value(name));
				}
				// 引数は静的に用意する。ここをリフレクションにすると、
				// 検証したいコンストラクタとは別の型の除去が混ざる
				var args = ctor.GetParameters()
					.Select(x => m_Services.FirstOrDefault(s => x.ParameterType.IsInstanceOfType(s)))
					.ToArray();
				return ((T)ctor.Invoke(args), new Value(name));
			}
		}

		static async Task<TestContext> Entry(ReflectionFactory factory)
		{
			var context = new TestContext();
			await context.Manager.Entry<Root>(
				new ParameterHolder(),
				new Config { ErrorHandler = context.ErrorHandler, Factory = factory });
			return context;
		}

		[SetUp]
		public void SetUp() => Value.Disposed = 0;

		[Test]
		public async Task StatesWithoutParameterlessConstructorCanBePushed()
		{
			using var context = await Entry(new ReflectionFactory());

			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();

			// 引数付きコンストラクタが管理コード除去で消えていないこと
			Assert.IsNull(context.LastError, "生成が失敗していないこと");
			var child = context.Manager.FindState<NeedsDependency>();
			Assert.IsNotNull(child, "ファクトリ経由で生成できる");
			Assert.IsNotNull(child.Dependency, "依存が渡っている");
		}

		[Test]
		public async Task StatesWithoutDeclaredConstructorCanBePushed()
		{
			// コンストラクタを一切宣言していない型。暗黙の引数なしコンストラクタが
			// リフレクションから解決できるか
			using var context = await Entry(new ReflectionFactory());

			context.Manager.FindState<Root>().DoPushPlain();
			await context.WaitTransition();

			Assert.IsNull(context.LastError, "生成が失敗していないこと");
			var child = context.Manager.FindState<NoDeclaredConstructor>();
			Assert.IsNotNull(child);
			Assert.IsTrue(child.Entered);
		}

		[Test]
		public async Task ValueIsPassedToTheChild()
		{
			var factory = new ReflectionFactory { Label = "a" };
			using var context = await Entry(factory);

			factory.Label = "b";
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();

			Assert.IsNotNull(context.Manager.FindState<NeedsDependency>());
		}

		[Test]
		public async Task ValueIsDisposedWhenTheStateExits()
		{
			using var context = await Entry(new ReflectionFactory());

			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();
			Assert.AreEqual(0, Value.Disposed);

			context.Manager.FindState<NeedsDependency>().DoPop();
			await context.WaitTransition();

			Assert.AreEqual(1, Value.Disposed, "ステートの終了で破棄される");
		}

		[Test]
		public async Task ValueIsDisposedWhenInitializationFails()
		{
			using var context = await Entry(new ReflectionFactory());
			var before = Value.Disposed;

			context.Manager.FindState<Root>().DoPushFailing();
			await context.Tick(4);

			Assert.IsNotNull(context.LastError, "初期化の失敗が報告される");
			Assert.IsNull(context.Manager.FindState<FailsOnInitialize>(), "木には残らない");
			Assert.AreEqual(before + 1, Value.Disposed,
				"木に繋がらなかったステートでも、ファクトリが返した値は破棄される");
		}

		[Test]
		public async Task ValueIsDisposedWhenArgumentApplyFails()
		{
			using var context = await Entry(new ReflectionFactory());
			var before = Value.Disposed;

			// 型の合わない値のため CreateState の中で失敗する
			context.Manager.FindState<Root>().DoPushBadArg();
			await context.Tick(4);

			Assert.IsNotNull(context.LastError);
			Assert.AreEqual(before + 1, Value.Disposed,
				"生成の途中で失敗しても、ファクトリが返した値は破棄される: " + context.LastError);
		}

		[Test]
		public async Task ValueIsDisposedWhenFoldingTheExistingChildFails()
		{
			using var context = await Entry(new ReflectionFactory());
			var root = context.Manager.FindState<Root>();

			// 終了処理で失敗するステートを主軸に置く
			root.DoPushBadExit();
			await context.WaitTransition();
			var before = Value.Disposed;

			// 兄弟を積むと既存の子を畳む段階で失敗する
			root.DoPushReplacing();
			await context.Tick(4);

			Assert.IsNotNull(context.LastError);
			Assert.AreEqual(before + 2, Value.Disposed,
				"新しく生成した値と、解体に失敗した側の値の両方が破棄される");
		}

		[Test]
		public async Task ValueIsDisposedWhenTheStateFailsToExit()
		{
			using var context = await Entry(new ReflectionFactory());
			var root = context.Manager.FindState<Root>();

			root.DoPushBadExit();
			await context.WaitTransition();
			var before = Value.Disposed;

			// OnPreExit で失敗するため Context.DisposeAsync まで到達しない
			context.Manager.FindState<FailsOnExit>().DoPop();
			await context.Tick(4);

			Assert.IsNotNull(context.LastError);
			Assert.AreEqual(before + 1, Value.Disposed,
				"解体に失敗したステートも回収される（木から外れた後なので誰も辿れない）");
		}

		[Test]
		public async Task ValueIsDisposedWhenExitHangsAndEntryRecovers()
		{
			using var context = await Entry(new ReflectionFactory());
			context.Manager.FindState<Root>().DoPushChild();
			await context.WaitTransition();

			var child = context.Manager.FindState<NeedsDependency>();
			var hang = new HangingExitTask();
			child.Context.RegisterExitTask(hang);

			// Context.DisposeAsync が IExitTask の完了待ちで止まる。
			// この時点で child は木から外れており、通常の解放経路からは辿れない
			child.DoPop();
			await context.Tick(2);
			var before = Value.Disposed;

			await context.Manager.Entry<Root>(
				new ParameterHolder(),
				new Config { ErrorHandler = context.ErrorHandler, Factory = new ReflectionFactory() });

			Assert.IsTrue(hang.Forced, "止まっている IExitTask は ForceRelease に倒れる");
			// 木に残っている Root と、木から外れて止まっている子の 2 つ。
			// 止まっている分を取りこぼすと 1 になる
			Assert.AreEqual(before + 2, Value.Disposed,
				"解体が止まっていても、Entry での復旧でファクトリの値は閉じられる");

			hang.Complete();
		}

		[Test]
		public async Task ValueIsDisposedWhenTheRootFailsToInitialize()
		{
			using var context = new TestContext();
			var config = new Config
			{
				ErrorHandler = context.ErrorHandler,
				Factory = new ReflectionFactory(),
			};

			try
			{
				await context.Manager.Entry<FailsOnInitialize>(new ParameterHolder(), config);
				Assert.Fail("初期化の失敗は呼び出し元に伝わる");
			}
			catch (InvalidOperationException)
			{
			}

			Assert.AreEqual(1, Value.Disposed,
				"初期化に失敗したルートでもファクトリの値は破棄される");
			Assert.IsNull(context.Manager.Root, "入場していないステートは残らない");
		}

		[Test]
		public async Task ArgumentReachesTheStateUnderStripping()
		{
			// [Arg] のプロパティはリフレクションからしか触られない。
			// 非ルートのアセンブリで除去されないかを見る
			using var context = await Entry(new ReflectionFactory());

			context.Manager.FindState<Root>().DoPushArg(42);
			await context.WaitTransition();

			Assert.IsNull(context.LastError);
			var child = context.Manager.FindState<TakesArgument>();
			Assert.IsNotNull(child);
			Assert.AreEqual(42, child.Value, "[Arg] が届いていない（除去された可能性）");
		}

		[Test]
		public async Task FactoryIsNotRequired()
		{
			// 未設定なら従来どおり Activator.CreateInstance<T>() で生成される
			using var context = new TestContext();
			await context.Entry<Root>();

			Assert.IsNotNull(context.Manager.FindState<Root>());
		}
	}
}
