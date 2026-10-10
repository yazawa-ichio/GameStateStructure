# 非同期・後始末・エラー

## 非同期処理

### 完了を待たない処理

遷移関数は `void` を返すので、`await` を含む処理は `Handle` に渡して実行します。

```cs
void OnStartButton()
{
	Handle(async () =>
	{
		await LoadAsync(Context.DisposeCancellationToken);
		PushInGame<InGameScene>();
	});
}
```

待つときは `Context.DisposeCancellationToken` を渡してください。
渡さないと、ステートが終了したあとも処理が続き、終了したステートから遷移しようとします。
そのとき `PushXxx` などは何もしませんが、`RunXxx` は呼び出し元に例外を返します。

- 例外は `ErrorHandler` に届きます。
- 第2引数 `ignoreCancel` は既定で `true` です。親の終了による取り消しは無視されます。
- 取り消しを自分で扱いたいときは、ラムダの中で `try` / `catch` してください。

### UniTask

UniTask を入れていると、`Handle` は `Func<UniTask>` を受け取ります。
`Handle(async () => ...)` の書き方はそのまま使えます。

`Func<Task>` は渡せません。`Handle(() => TaskReturning())` のように `async` でないラムダやメソッドを渡しているときは、
`Handle(async () => await TaskReturning())` に書き換えてください。

`GSS_DISABLE_UNITASK` を定義すると、`Task` 版に戻ります。

## 後始末

### 長い待ちを止める

ステートの中で時間のかかる処理を待つときは、`Context.DisposeCancellationToken` を渡してください。
ステートが終了すると取り消されます。

```cs
protected override async Task OnInitialize()
{
	m_Data = await LoadAsync(Context.DisposeCancellationToken);
}
```

取り消されるのは、**終了処理がすべて終わったあと**です。
`IExitTask` や `OnExit` より前に止めたいときは、`Active` を確認してください。

### 解放の登録

解放したいものは `Context.Manage` に登録してください。これが基本の方法です。
登録したものは、`Pop`・`GoTo` に加えて、`Entry` のやり直しでも解放されます。

`OnPreExit` と `OnExit` にも後始末を書けます。
ただしこの2つは通常の終了でだけ呼ばれ、`Entry` のやり直しでは呼ばれません。
エラーからの復旧で `Entry` をやり直す場合は注意してください。

### 終了時の演出（IExitTask）

UI の閉じる演出のように、終了時に非同期の処理を挟みたいときは、`IExitTask` を実装して `Context.RegisterExitTask` に登録します。
`OnPreExit` のあと、`OnExit` の前に実行されます。

```cs
class UIClose : IExitTask
{
	IStackEntry m_Entry;

	public UIClose(IStackEntry entry) => m_Entry = entry;

	public async Task Run(CancellationToken ct)
	{
		// 閉じる演出が終わるのを待つ
		await m_Entry.Pop(ct);
	}

	public void ForceRelease()
	{
		// 演出なしですぐ閉じる
		m_Entry.PopImmediate();
	}
}

public IStackEntry PushUI(string path, ViewModel vm)
{
	var entry = UIStack.Push(path, vm);
	Context.RegisterExitTask(new UIClose(entry));
	return entry;
}
```

決まりごとは次のとおりです。

- `Entry` のやり直しのように待てない場合は、`Run` の代わりに `ForceRelease` が呼ばれます。演出はせず、最低限の後始末を同期で行ってください。
- **`Run` と `ForceRelease` の両方が呼ばれることがあります。** `Run` を待っている途中で強制解放が来ると、`ForceRelease` も呼ばれます。`ForceRelease` は `Run` の途中で呼ばれても問題ないように書いてください。
- 複数登録すると、`Run` は**同時に**実行されます。順番が必要なら、1つの `IExitTask` にまとめてください。
- `Run` に渡される `CancellationToken` は**常に `default`** です。終了処理を外から取り消す仕組みはありません。

使い分けは次のとおりです。

| やりたいこと | 使うもの |
| --- | --- |
| 解放する | `Context.Manage` |
| 終了時に演出する | `Context.RegisterExitTask` |

## エラーと復旧

遷移の途中で例外が出ると、`ErrorHandler` に届き、**以後の遷移は止まります。**
止まったあとに遷移しようとすると `TransitionHangException` になります。

元に戻す方法は、`Entry` をやり直すことだけです。

```cs
async void IErrorHandler.Handle(Exception ex)
{
	try
	{
		// 表示や記録をしてから作り直す
		await Manager.Entry<BootState>();
	}
	catch (Exception e)
	{
		// 作り直しが失敗した場合。ここで再び Entry すると、失敗が繰り返される
		Debug.LogException(e);
	}
}
```

### Entry の失敗

**`Entry` が返す Task は、必ず `await` などで結果を確認してください。**
最初のステートの初期化や `OnEnter` で出た例外は、この Task にだけ入り、`ErrorHandler` には届きません。
`ErrorHandler` は `Entry` を呼び直す場所なので、そこへ `Entry` の失敗を送ると、やり直しが止まらなくなるためです。

### やり直し前の遷移

`Entry` は、今あるステートをすべて解放してから作り直します。
このとき、途中だった遷移は**打ち切られます。**

- 打ち切られた遷移は `TransitionAbortedException`（`OperationCanceledException` の派生）で終わります。
- 作り直したあとのステートにも、`ErrorHandler` にも影響しません。
- `RunXxx` を `Handle` を通さずに直接 `await` しているときは、この例外が呼び出し元に届くことがあります。
