namespace GameStateStructure.Fixtures
{
	/// <summary>
	/// 管理コード除去の検証用。
	/// <para>
	/// テストアセンブリはリンカのルートに指定されるため中身が除去されない。
	/// 除去を再現するには、ルートでないアセンブリに置く必要がある。
	/// </para>
	/// </summary>
	public class Dependency
	{
	}

	/// <summary>
	/// 型の合わない値を渡すと <c>ParameterHolder.Apply</c> が投げる。
	/// <c>CreateState</c> の中で例外になる経路の検証用。
	/// </summary>
	public class FailsOnArgument : GameState
	{
		// [Push] で宣言せず public API から直接積むため、生成器の保持対象にならない。
		// 検証したいのは解放の有無なので、ここは明示的に保持する。
		[UnityEngine.Scripting.Preserve]
		public FailsOnArgument() { }

		[Arg]
		public int Value { get; set; }
	}

	/// <summary>[Arg] が管理コード除去を越えて届くかを見るための型。</summary>
	public class TakesArgument : GameState
	{
		[Arg]
		public int Value { get; set; }
	}

	/// <summary>終了処理で失敗する。畳む側が例外を投げる経路の検証用。</summary>
	public class FailsOnExit : GameState
	{
		public void DoPop() => Pop();

		protected override System.Threading.Tasks.Task OnPreExit()
			=> throw new System.InvalidOperationException("exit failed");
	}

	/// <summary>初期化で失敗する。遷移が途中で止まる経路の検証用。</summary>
	public class FailsOnInitialize : GameState
	{
		protected override System.Threading.Tasks.Task OnInitialize()
			=> throw new System.InvalidOperationException("initialize failed");
	}

	/// <summary>
	/// コンストラクタを一切宣言していない。暗黙の引数なしコンストラクタが
	/// 管理コード除去で消えるかを見るための型。
	/// </summary>
	public class NoDeclaredConstructor : GameState
	{
		public bool Entered { get; private set; }

		protected override void OnEnter() => Entered = true;
	}

	/// <summary>
	/// 引数なしコンストラクタを持たない。コンストラクタはリフレクションからしか
	/// 呼ばれないため、静的な参照が無いと除去される。
	/// </summary>
	public class NeedsDependency : GameState
	{
		public Dependency Dependency { get; }

		public NeedsDependency(Dependency dependency) => Dependency = dependency;

		public void DoPop() => Pop();
	}
}
