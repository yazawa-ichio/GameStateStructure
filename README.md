# GameStateStructure

ゲーム全体の流れ（タイトル、インゲーム、ダイアログなど）をステートとして管理する Unity 向けのライブラリです。
遷移を属性で宣言するので、どこからどこへ遷移するかがコードから読み取れます。

## 導入

Package Manager の「Add package from git URL」に次の URL を入力します。

```
https://github.com/yazawa-ichio/GameStateStructure.git?path=Packages/jp.yazawa-ichio.game-state-structure
```

## 使い方

`GameState` を継承したクラスに、遷移先を属性で書きます。
遷移関数（`PushBoot` など）は自動で生成されます。生成のため、クラスは `partial` にしてください。

```cs
[GoTo(typeof(Root))]
[Push(typeof(Boot))]
partial class Root : GameState
{
	protected override void OnEnter()
	{
		PushBoot<Boot>();
	}

	public void Reboot()
	{
		GoToRoot<Root>();
	}
}

[Push(typeof(Dialog))]
partial class Boot : GameState
{
	public async void OpenDialog()
	{
		await RunDialog<Dialog>("Message");
	}
}

partial class Dialog : GameState, IProcess
{
	[Arg]
	string Message { get; set; }

	public async Task Run(CancellationToken token)
	{
		await OpenUI(Message, token);
	}
}
```

`GameStateManager` に最初のステートを渡して開始します。

```cs
var manager = gameObject.AddComponent<GameStateManager>();
await manager.Entry<Root>(new Config { ErrorHandler = this });
```

## ドキュメント

| ファイル | 内容 |
| --- | --- |
| [ステートと遷移](docs/state.md) | 遷移の宣言、引数、ブランチ（Branch）、Process、子の破棄通知、イベント、共通処理（Decorator） |
| [非同期・後始末・エラー](docs/lifecycle.md) | `Handle`と UniTask、終了時の解放と演出（IExitTask）、エラーからの復旧 |
| [拡張とツール](docs/tools.md) | ステートの生成の差し替えと DI、ステート木の取得と遷移の監視、EditorWindow、遷移図、ログ |
